using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Users;
using Microsoft.Extensions.DependencyInjection;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Me;

[Collection(PostgresCollection.Name)]
public class AuthorProfileTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private Domain.Entities.User _ann = null!;
    private Domain.Entities.User _bob = null!;
    private string _annToken = null!;
    private string _bobToken = null!;
    private string _adminToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _ann = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _bob = await _api.SeedUserAsync("bob@demo.local", UserRole.Author, fullName: "Bob Writer");
        await _api.ExecuteAsync("UPDATE users SET bio = 'Ann bio' WHERE id = @id", ("id", _ann.Id));
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _annToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
        _bobToken = await _api.LoginTokenAsync("bob@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private Task<HttpResponseMessage> GetAsync(string? token = null) =>
        _api.SendAsync(HttpMethod.Get, "/api/me/profile", token ?? _annToken);

    private Task<HttpResponseMessage> PutAsync(object body, string? token = null) =>
        _api.SendJsonAsync(HttpMethod.Put, "/api/me/profile", token ?? _annToken, body);

    private async Task<uint> VersionAsync(string? token = null) =>
        (await ReadAsync(await GetAsync(token))).GetProperty("version").GetUInt32();

    private async Task<HttpResponseMessage> EditAsync(string? fullName, string? bio) =>
        await PutAsync(new { fullName, bio, version = await VersionAsync() });

    private Task<string> ColumnAsync(string column, Guid? id = null) =>
        _api.ScalarAsync<string>($"SELECT coalesce({column}::text, '<null>') FROM users WHERE id = @id", ("id", id ?? _ann.Id));

    // ---- read ----

    [Fact]
    public async Task An_author_reads_their_own_profile()
    {
        var response = await GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(_ann.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Ann Author", body.GetProperty("fullName").GetString());
        Assert.Equal("ann@demo.local", body.GetProperty("email").GetString());
        Assert.Equal("Ann bio", body.GetProperty("bio").GetString());
        Assert.Equal("Author", body.GetProperty("role").GetString());
        Assert.Equal(_ann.CreatedAt, body.GetProperty("registeredAt").GetDateTimeOffset());
        Assert.True(body.GetProperty("version").GetUInt32() > 0);
    }

    [Fact]
    public async Task The_profile_has_no_password_or_status_data()
    {
        var names = (await ReadAsync(await GetAsync())).EnumerateObject().Select(p => p.Name).Order().ToArray();

        Assert.Equal(["bio", "email", "fullName", "id", "registeredAt", "role", "version"], names);
    }

    [Fact]
    public async Task Each_author_gets_only_their_own_profile()
    {
        var ann = await ReadAsync(await GetAsync(_annToken));
        var bob = await ReadAsync(await GetAsync(_bobToken));

        Assert.Equal("ann@demo.local", ann.GetProperty("email").GetString());
        Assert.Equal("bob@demo.local", bob.GetProperty("email").GetString());
    }

    [Fact]
    public async Task The_profile_cannot_be_redirected_to_another_author_by_the_request()
    {
        var byQuery = await _api.SendAsync(HttpMethod.Get, $"/api/me/profile?id={_bob.Id}&userId={_bob.Id}", _annToken);
        var byRoute = await _api.SendAsync(HttpMethod.Get, $"/api/me/profile/{_bob.Id}", _annToken);
        var viaUsers = await _api.SendAsync(HttpMethod.Get, $"/api/users/{_bob.Id}", _annToken);
        var viaAdmin = await _api.SendAsync(HttpMethod.Get, $"/api/admin/users/{_bob.Id}", _annToken);

        Assert.Equal(_ann.Id, (await ReadAsync(byQuery)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, byRoute.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaUsers.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viaAdmin.StatusCode);
    }

    // ---- update ----

    [Fact]
    public async Task A_valid_update_persists_and_returns_the_new_profile()
    {
        _api.Clock.Advance(TimeSpan.FromHours(1));
        var before = await VersionAsync();

        var response = await PutAsync(new { fullName = "  Ann B. Author ", bio = "New bio.", version = before });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Ann B. Author", body.GetProperty("fullName").GetString());
        Assert.Equal("New bio.", body.GetProperty("bio").GetString());
        Assert.NotEqual(before, body.GetProperty("version").GetUInt32());
        Assert.Equal("Ann B. Author", await ColumnAsync("full_name"));
        Assert.Equal("New bio.", await ColumnAsync("bio"));
        Assert.Equal("Ann B. Author", (await ReadAsync(await GetAsync())).GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task The_new_version_can_be_used_for_the_next_update()
    {
        var first = await ReadAsync(await PutAsync(new { fullName = "Ann One", bio = "x", version = await VersionAsync() }));

        var second = await PutAsync(new { fullName = "Ann Two", bio = "y", version = first.GetProperty("version").GetUInt32() });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_biography_clears_it(string? bio)
    {
        var response = await EditAsync("Ann Author", bio);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(response)).GetProperty("bio").ValueKind);
        Assert.Equal("<null>", await ColumnAsync("bio"));
    }

    [Fact]
    public async Task The_limits_are_inclusive()
    {
        var response = await EditAsync(new string('n', 100), new string('b', 1000));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, (await ColumnAsync("full_name")).Length);
        Assert.Equal(1000, (await ColumnAsync("bio")).Length);
    }

    [Fact]
    public async Task Markup_in_the_profile_is_stored_as_typed_and_returned_as_data()
    {
        var response = await EditAsync("Ann <b>Author</b>", "<img src=x onerror=alert(1)>\nline two");

        var body = await ReadAsync(response);
        Assert.Equal("Ann <b>Author</b>", body.GetProperty("fullName").GetString());
        Assert.Equal("<img src=x onerror=alert(1)>\nline two", body.GetProperty("bio").GetString());
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Email_role_status_password_and_id_in_the_body_are_ignored()
    {
        var hash = await ColumnAsync("password_hash");

        var response = await PutAsync(new
        {
            fullName = "Ann Renamed",
            bio = "Bio",
            version = await VersionAsync(),
            email = "hacker@evil.example",
            normalizedEmail = "hacker@evil.example",
            role = "Administrator",
            status = "Suspended",
            suspensionReason = "x",
            password = "Hacked!Passw0rd#1",
            passwordHash = "plain",
            id = _bob.Id,
            userId = _bob.Id,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("ann@demo.local", body.GetProperty("email").GetString());
        Assert.Equal("Author", body.GetProperty("role").GetString());
        Assert.Equal(_ann.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("ann@demo.local", await ColumnAsync("email"));
        Assert.Equal("ann@demo.local", await ColumnAsync("normalized_email"));
        Assert.Equal("Author", await ColumnAsync("role"));
        Assert.Equal("Active", await ColumnAsync("status"));
        Assert.Equal("<null>", await ColumnAsync("suspension_reason"));
        Assert.Equal(hash, await ColumnAsync("password_hash"));
        Assert.Equal("Bob Writer", await ColumnAsync("full_name", _bob.Id));
        Assert.Equal(HttpStatusCode.OK, (await _api.LoginAsync("ann@demo.local", ApiHost.Password, "Author")).StatusCode);
    }

    [Fact]
    public async Task An_update_never_touches_another_author()
    {
        await EditAsync("Ann Renamed", "Changed");

        Assert.Equal("Bob Writer", await ColumnAsync("full_name", _bob.Id));
        Assert.Equal("<null>", await ColumnAsync("bio", _bob.Id));
    }

    // ---- validation ----

    [Theory]
    [InlineData("", "Full name is required.")]
    [InlineData("   ", "Full name is required.")]
    [InlineData("A", "Full name must be between 2 and 100 characters.")]
    [InlineData("  A  ", "Full name must be between 2 and 100 characters.")]
    public async Task An_invalid_name_is_a_400_and_nothing_changes(string name, string message)
    {
        var response = await EditAsync(name, "Changed bio");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.Equal([message], errors.GetProperty("fullName").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Ann Author", await ColumnAsync("full_name"));
        Assert.Equal("Ann bio", await ColumnAsync("bio"));
    }

    [Fact]
    public async Task A_name_of_101_characters_and_a_biography_of_1001_are_reported_together()
    {
        var response = await EditAsync(new string('n', 101), new string('b', 1001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.Equal("Full name must be between 2 and 100 characters.", errors.GetProperty("fullName")[0].GetString());
        Assert.Equal("Biography must be at most 1000 characters.", errors.GetProperty("bio")[0].GetString());
        Assert.Equal("Ann Author", await ColumnAsync("full_name"));
    }

    [Fact]
    public async Task A_missing_name_or_version_is_a_400()
    {
        var noName = await PutAsync(new { bio = "x", version = await VersionAsync() });
        var noVersion = await PutAsync(new { fullName = "Ann Author", bio = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        Assert.True((await ReadAsync(noVersion)).GetProperty("errors").TryGetProperty("version", out _));
    }

    // ---- concurrency ----

    [Fact]
    public async Task A_stale_version_is_a_409_and_changes_nothing()
    {
        var stale = await VersionAsync();
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(new { fullName = "Ann First", bio = "One", version = stale })).StatusCode);

        var response = await PutAsync(new { fullName = "Ann Second", bio = "Two", version = stale });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed by someone else", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Equal("Ann First", await ColumnAsync("full_name"));
    }

    [Fact]
    public async Task A_change_by_an_administrator_makes_the_authors_version_stale()
    {
        var annVersion = await VersionAsync();
        var adminVersion = (await ReadAsync(
            await _api.SendAsync(HttpMethod.Get, $"/api/admin/users/{_ann.Id}", _adminToken))).GetProperty("version").GetUInt32();
        await _api.SendJsonAsync(HttpMethod.Put, $"/api/admin/users/{_ann.Id}/profile", _adminToken,
            new { fullName = "Ann By Admin", bio = "x", version = adminVersion });

        var response = await PutAsync(new { fullName = "Ann Late", bio = "y", version = annVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Ann By Admin", await ColumnAsync("full_name"));
    }

    [Fact]
    public async Task Two_simultaneous_updates_with_the_same_version_let_exactly_one_win()
    {
        var version = await VersionAsync();

        var responses = await Task.WhenAll(
            PutAsync(new { fullName = "Ann Alpha", bio = "A", version }),
            PutAsync(new { fullName = "Ann Beta", bio = "B", version }));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    // ---- authorization ----

    [Fact]
    public async Task An_administrator_token_gets_403_on_both_endpoints_and_nothing_changes()
    {
        var get = await GetAsync(_adminToken);
        var put = await PutAsync(new { fullName = "Hijacked Admin", bio = "x", version = 1 }, _adminToken);

        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal("Alice Admin", await ColumnAsync("full_name", (await _api.ScalarAsync<Guid>(
            "SELECT id FROM users WHERE role = 'Administrator'"))));
    }

    [Fact]
    public async Task A_visitor_gets_401_and_a_suspended_authors_token_stops_working()
    {
        var anonymousGet = await _api.SendAsync(HttpMethod.Get, "/api/me/profile", null);
        var anonymousPut = await _api.SendJsonAsync(HttpMethod.Put, "/api/me/profile", null, new { fullName = "X Y", version = 1 });
        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE id = @id", ("id", _ann.Id));
        var suspended = await GetAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousGet.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, suspended.StatusCode);
    }

    [Fact]
    public async Task The_service_itself_refuses_an_administrators_id_even_if_a_caller_forgot_the_policy()
    {
        var adminId = await _api.ScalarAsync<Guid>("SELECT id FROM users WHERE role = 'Administrator'");
        using var scope = _api.Services.CreateScope();
        var profiles = scope.ServiceProvider.GetRequiredService<IAuthorProfileService>();

        await Assert.ThrowsAsync<NotFoundException>(() => profiles.GetAsync(adminId));
        await Assert.ThrowsAsync<NotFoundException>(() => profiles.UpdateAsync(adminId, "Hijacked Admin", "x", 1));
        await Assert.ThrowsAsync<NotFoundException>(() => profiles.GetAsync(Guid.NewGuid()));
        Assert.Equal("Alice Admin", await ColumnAsync("full_name", adminId));
    }
}
