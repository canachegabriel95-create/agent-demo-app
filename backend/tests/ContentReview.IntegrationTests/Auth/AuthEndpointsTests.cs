using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Auth;

[Collection(PostgresCollection.Name)]
public class AuthEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AdminEmail = "admin@demo.local";
    private const string AuthorEmail = "author@demo.local";

    private ApiHost _api = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        await _api.SeedUserAsync(AdminEmail, UserRole.Administrator, fullName: "Platform Admin");
        await _api.SeedUserAsync(AuthorEmail, UserRole.Author, fullName: "Jane Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>The ProblemDetails fields that must be identical for every login failure (traceId differs per request).</summary>
    private static async Task<(int Status, string? Title, string? Type, string? Detail)> FailureShapeAsync(
        HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        string? Get(string name) => body.TryGetProperty(name, out var value) ? value.GetString() : null;
        return ((int)response.StatusCode, Get("title"), Get("type"), Get("detail"));
    }

    [Theory]
    [InlineData(AdminEmail, "Admin", "Administrator")]
    [InlineData(AuthorEmail, "Author", "Author")]
    public async Task Valid_login_returns_a_token_expiry_and_the_user_summary(string email, string portal, string role)
    {
        var response = await _api.LoginAsync(email, ApiHost.Password, portal);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(43, body.GetProperty("token").GetString()!.Length); // 32 bytes, base64url
        Assert.Equal(_api.Clock.GetUtcNow().AddHours(8), body.GetProperty("expiresAt").GetDateTimeOffset());
        var user = body.GetProperty("user");
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal(role, user.GetProperty("role").GetString());
        Assert.False(user.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task Login_email_is_case_and_whitespace_insensitive()
    {
        var response = await _api.LoginAsync("  Admin@Demo.LOCAL ", ApiHost.Password, "Admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Every_login_failure_returns_the_same_401()
    {
        await _api.SeedUserAsync("suspended@demo.local", UserRole.Author, suspended: true);

        var failures = new[]
        {
            await _api.LoginAsync(AdminEmail, "Wrong!Passw0rd#1", "Admin"),            // wrong password
            await _api.LoginAsync("nobody@demo.local", ApiHost.Password, "Admin"),     // unknown email
            await _api.LoginAsync("not-an-email", ApiHost.Password, "Admin"),          // malformed email
            await _api.LoginAsync(AuthorEmail, ApiHost.Password, "Admin"),             // author on the admin portal
            await _api.LoginAsync(AdminEmail, ApiHost.Password, "Author"),             // admin on the author portal
            await _api.LoginAsync("suspended@demo.local", ApiHost.Password, "Author"), // suspended
        };

        var shapes = new List<(int, string?, string?, string?)>();
        foreach (var response in failures)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            shapes.Add(await FailureShapeAsync(response));
        }

        Assert.All(shapes, shape => Assert.Equal(shapes[0], shape));
        Assert.Equal("Invalid email or password.", shapes[0].Item2);
    }

    [Theory]
    [InlineData(null, "x", "Admin")]
    [InlineData("a@b.co", null, "Admin")]
    [InlineData("a@b.co", "x", null)]
    [InlineData("a@b.co", "x", "Superuser")]
    public async Task Malformed_requests_are_rejected_with_400(string? email, string? password, string? portal)
    {
        var response = await _api.LoginAsync(email, password, portal);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_sha256_hash_of_the_token_is_stored()
    {
        var token = await _api.LoginTokenAsync(AdminEmail, "Admin");

        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        Assert.Equal(expected, await _api.ScalarAsync<string>("SELECT token_hash FROM user_sessions"));
        Assert.Equal(0L, await _api.ScalarAsync<long>(
            "SELECT count(*) FROM user_sessions WHERE token_hash = @t", ("t", token)));
    }

    [Fact]
    public async Task Me_returns_the_current_user()
    {
        var token = await _api.LoginTokenAsync(AuthorEmail, "Author");

        var response = await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(AuthorEmail, body.GetProperty("email").GetString());
        Assert.Equal("Jane Author", body.GetProperty("fullName").GetString());
        Assert.Equal("Author", body.GetProperty("role").GetString());
        Assert.Equal("Active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Logout_returns_204_and_the_token_stops_working()
    {
        var token = await _api.LoginTokenAsync(AdminEmail, "Admin");

        var logout = await _api.SendAsync(HttpMethod.Post, "/api/auth/logout", token);
        var me = await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token);
        var logoutAgain = await _api.SendAsync(HttpMethod.Post, "/api/auth/logout", token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, logoutAgain.StatusCode);
        Assert.Equal(1L, await _api.ScalarAsync<long>("SELECT count(*) FROM user_sessions WHERE revoked_at IS NOT NULL"));
    }

    [Fact]
    public async Task Logout_ends_only_the_current_session()
    {
        var first = await _api.LoginTokenAsync(AdminEmail, "Admin");
        var second = await _api.LoginTokenAsync(AdminEmail, "Admin");

        await _api.SendAsync(HttpMethod.Post, "/api/auth/logout", first);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, "/api/auth/me", first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.SendAsync(HttpMethod.Get, "/api/auth/me", second)).StatusCode);
    }

    [Fact]
    public async Task An_expired_session_is_rejected()
    {
        var token = await _api.LoginTokenAsync(AdminEmail, "Admin");

        _api.Clock.Advance(TimeSpan.FromHours(7) + TimeSpan.FromMinutes(59));
        Assert.Equal(HttpStatusCode.OK, (await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token)).StatusCode);

        _api.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token)).StatusCode);
    }

    [Fact]
    public async Task Suspending_a_user_takes_effect_on_existing_sessions_immediately()
    {
        var token = await _api.LoginTokenAsync(AuthorEmail, "Author");
        Assert.Equal(HttpStatusCode.OK, (await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token)).StatusCode);

        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE normalized_email = @e", ("e", AuthorEmail));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token)).StatusCode);
    }

    [Fact]
    public async Task Missing_or_invalid_credentials_return_a_401_problem_with_a_bearer_challenge()
    {
        var token = await _api.LoginTokenAsync(AdminEmail, "Admin");

        var responses = new[]
        {
            await _api.SendAsync(HttpMethod.Get, "/api/auth/me", null),
            await _api.SendAsync(HttpMethod.Get, "/api/auth/me", "not-a-real-token"),
            await _api.SendAsync(HttpMethod.Get, "/api/auth/me", new string('x', 500)),
            await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token, scheme: "Basic"),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(401, (await ReadAsync(response)).GetProperty("status").GetInt32());
        }
    }

    [Fact]
    public async Task Last_seen_is_recorded_at_most_once_a_minute()
    {
        var token = await _api.LoginTokenAsync(AdminEmail, "Admin");
        Assert.True(await _api.ScalarAsync<bool>("SELECT last_seen_at IS NULL FROM user_sessions"));

        await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token);
        var first = await _api.ScalarAsync<DateTime>("SELECT last_seen_at FROM user_sessions");

        _api.Clock.Advance(TimeSpan.FromSeconds(30));
        await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token);
        Assert.Equal(first, await _api.ScalarAsync<DateTime>("SELECT last_seen_at FROM user_sessions"));

        _api.Clock.Advance(TimeSpan.FromSeconds(31));
        await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token);
        Assert.True(await _api.ScalarAsync<DateTime>("SELECT last_seen_at FROM user_sessions") > first);
    }

    [Fact]
    public async Task The_plain_password_and_token_are_not_returned_or_stored_anywhere_in_user_rows()
    {
        var token = await _api.LoginTokenAsync(AdminEmail, "Admin");

        Assert.Equal(0L, await _api.ScalarAsync<long>(
            "SELECT count(*) FROM users WHERE password_hash = @p", ("p", ApiHost.Password)));
        Assert.Equal(0L, await _api.ScalarAsync<long>(
            "SELECT count(*) FROM audit_events WHERE details::text LIKE @t", ("t", $"%{token}%")));
    }
}
