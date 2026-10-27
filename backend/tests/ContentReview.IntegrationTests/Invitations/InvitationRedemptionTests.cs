using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Invitations;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ContentReview.IntegrationTests.Invitations;

[Collection(PostgresCollection.Name)]
public class InvitationRedemptionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Email = "new.author@example.com";

    private ApiHost _api = null!;
    private User _admin = null!;
    private string _adminToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private async Task<(Guid Id, string Code)> CreateInvitationAsync(string email = Email, int? validForDays = null)
    {
        var response = await _api.SendJsonAsync(HttpMethod.Post, "/api/admin/invitations", _adminToken, new { email, validForDays });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("code").GetString()!);
    }

    private async Task<InvitationValidationResult> ValidateAsync(string? code, string? email)
    {
        using var scope = _api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInvitationRedemptionService>().ValidateAsync(code, email);
    }

    /// <summary>Consumes inside a transaction, as registration does, and commits when it succeeds.</summary>
    private async Task ConsumeAsync(string? code, string? email, Guid userId, bool commit = true)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentReviewDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IInvitationRedemptionService>().ConsumeAsync(code, email, userId);
        if (commit)
        {
            await transaction.CommitAsync();
        }
    }

    private async Task<bool> IsUsedAsync(Guid invitationId) =>
        await _api.ScalarAsync<bool>("SELECT used_at IS NOT NULL OR used_by_user_id IS NOT NULL FROM invitations WHERE id = @id", ("id", invitationId));

    // ---- validate ----

    [Fact]
    public async Task A_valid_code_for_its_email_validates_without_being_consumed()
    {
        var (id, code) = await CreateInvitationAsync();

        Assert.Equal(InvitationValidationResult.Valid, await ValidateAsync(code, Email));
        Assert.Equal(InvitationValidationResult.Valid, await ValidateAsync(code, Email));
        Assert.False(await IsUsedAsync(id));
    }

    [Theory]
    [InlineData("lower")]
    [InlineData("nodashes")]
    [InlineData("padded")]
    [InlineData("lower-nodashes")]
    public async Task The_code_is_accepted_in_any_case_with_or_without_dashes(string variant)
    {
        var (_, code) = await CreateInvitationAsync();
        var input = variant switch
        {
            "lower" => code.ToLowerInvariant(),
            "nodashes" => code.Replace("-", string.Empty),
            "padded" => $"  {code}  ",
            _ => code.Replace("-", string.Empty).ToLowerInvariant(),
        };

        Assert.Equal(InvitationValidationResult.Valid, await ValidateAsync(input, Email));
    }

    [Theory]
    [InlineData("  NEW.Author@Example.com ")]
    [InlineData("new.author@example.com")]
    public async Task The_email_is_compared_in_its_normalized_form(string email)
    {
        var (_, code) = await CreateInvitationAsync("New.Author@EXAMPLE.com");

        Assert.Equal(InvitationValidationResult.Valid, await ValidateAsync(code, email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-code")]
    [InlineData("0000-0000-0000")]
    [InlineData("ABCD-EFGH-JKM")]
    public async Task An_unknown_or_malformed_code_is_not_found(string? code)
    {
        await CreateInvitationAsync();

        Assert.Equal(InvitationValidationResult.NotFound, await ValidateAsync(code, Email));
    }

    [Theory]
    [InlineData("other@example.com")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Another_email_is_a_mismatch(string? email)
    {
        var (_, code) = await CreateInvitationAsync();

        Assert.Equal(InvitationValidationResult.EmailMismatch, await ValidateAsync(code, email));
    }

    [Fact]
    public async Task Each_failure_reason_is_reported_by_validate()
    {
        var (revokedId, revoked) = await CreateInvitationAsync("revoked@example.com");
        var (_, used) = await CreateInvitationAsync("used@example.com");
        var (_, expired) = await CreateInvitationAsync("expired@example.com", 1);
        var (_, available) = await CreateInvitationAsync("available@example.com", 30);
        var author = await _api.SeedUserAsync("used@example.com", UserRole.Author);

        // Mark the Used one through the real path, then revoke and let the clock run out.
        await ConsumeAsync(used, "used@example.com", author.Id);
        var version = (await (await _api.SendAsync(HttpMethod.Get, $"/api/admin/invitations/{revokedId}", _adminToken)).Content
            .ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32();
        (await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/invitations/{revokedId}/revoke", _adminToken, new { version })).EnsureSuccessStatusCode();
        _api.Clock.Advance(TimeSpan.FromDays(2));

        Assert.Equal(InvitationValidationResult.AlreadyUsed, await ValidateAsync(used, "used@example.com"));
        Assert.Equal(InvitationValidationResult.Revoked, await ValidateAsync(revoked, "revoked@example.com"));
        Assert.Equal(InvitationValidationResult.Expired, await ValidateAsync(expired, "expired@example.com"));
        Assert.Equal(InvitationValidationResult.Valid, await ValidateAsync(available, "available@example.com"));
    }

    [Fact]
    public async Task A_wrong_email_learns_nothing_about_the_status_of_the_code()
    {
        var (id, code) = await CreateInvitationAsync();
        await _api.ExecuteAsync("UPDATE invitations SET revoked_at = now(), revoked_by_user_id = @admin WHERE id = @id", ("admin", _admin.Id), ("id", id));

        Assert.Equal(InvitationValidationResult.EmailMismatch, await ValidateAsync(code, "someone.else@example.com"));
        Assert.Equal(InvitationValidationResult.Revoked, await ValidateAsync(code, Email));
    }

    [Fact]
    public async Task A_code_expires_exactly_at_its_expiry_time()
    {
        var (id, code) = await CreateInvitationAsync(validForDays: 1);
        var expiresAt = await _api.ScalarAsync<DateTime>("SELECT expires_at FROM invitations WHERE id = @id", ("id", id));

        _api.Clock.Advance(new DateTimeOffset(expiresAt, TimeSpan.Zero).AddTicks(-1) - _api.Clock.GetUtcNow());
        Assert.Equal(InvitationValidationResult.Valid, await ValidateAsync(code, Email));

        _api.Clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(InvitationValidationResult.Expired, await ValidateAsync(code, Email));
    }

    // ---- consume ----

    [Fact]
    public async Task Consume_sets_used_at_and_the_user_and_the_invitation_then_reads_as_used()
    {
        var (id, code) = await CreateInvitationAsync();
        var author = await _api.SeedUserAsync(Email, UserRole.Author, fullName: "Jane Author");
        _api.Clock.Advance(TimeSpan.FromHours(2));

        await ConsumeAsync(code.ToLowerInvariant().Replace("-", string.Empty), $" {Email.ToUpperInvariant()} ", author.Id);

        var body = await (await _api.SendAsync(HttpMethod.Get, $"/api/admin/invitations/{id}", _adminToken)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Used", body.GetProperty("status").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), body.GetProperty("usedAt").GetDateTimeOffset());
        Assert.Equal(author.Id, body.GetProperty("usedBy").GetProperty("id").GetGuid());
        Assert.Equal("Jane Author", body.GetProperty("usedBy").GetProperty("fullName").GetString());
        Assert.Equal(InvitationValidationResult.AlreadyUsed, await ValidateAsync(code, Email));
    }

    [Fact]
    public async Task Consume_is_undone_when_the_registration_transaction_rolls_back()
    {
        var (id, code) = await CreateInvitationAsync();
        var author = await _api.SeedUserAsync(Email, UserRole.Author);

        await ConsumeAsync(code, Email, author.Id, commit: false);

        Assert.False(await IsUsedAsync(id));
        Assert.Equal(InvitationValidationResult.Valid, await ValidateAsync(code, Email));
    }

    [Fact]
    public async Task Consume_outside_a_transaction_is_refused()
    {
        var (id, code) = await CreateInvitationAsync();
        var author = await _api.SeedUserAsync(Email, UserRole.Author);
        using var scope = _api.Services.CreateScope();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IInvitationRedemptionService>().ConsumeAsync(code, Email, author.Id));

        Assert.Contains("registration transaction", ex.Message);
        Assert.False(await IsUsedAsync(id));
    }

    [Fact]
    public async Task Ten_parallel_consumptions_of_the_same_code_let_exactly_one_succeed()
    {
        var (id, code) = await CreateInvitationAsync();
        var users = new List<User>();
        for (var i = 0; i < 10; i++)
        {
            users.Add(await _api.SeedUserAsync(Email.Replace("new", $"new{i}"), UserRole.Author));
        }

        var attempts = await Task.WhenAll(users.Select(async user =>
        {
            try
            {
                await ConsumeAsync(code, Email, user.Id);
                return (User: user, Reason: (InvitationValidationResult?)null);
            }
            catch (InvitationNotRedeemableException ex)
            {
                return (User: user, Reason: ex.Reason);
            }
        }));

        var winner = Assert.Single(attempts, a => a.Reason is null);
        Assert.Equal(9, attempts.Count(a => a.Reason == InvitationValidationResult.AlreadyUsed));
        Assert.Equal(winner.User.Id, await _api.ScalarAsync<Guid>("SELECT used_by_user_id FROM invitations WHERE id = @id", ("id", id)));
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM invitations WHERE used_by_user_id IS NOT NULL"));
    }

    [Theory]
    [InlineData(InvitationValidationResult.NotFound)]
    [InlineData(InvitationValidationResult.EmailMismatch)]
    [InlineData(InvitationValidationResult.Expired)]
    [InlineData(InvitationValidationResult.Revoked)]
    [InlineData(InvitationValidationResult.AlreadyUsed)]
    public async Task Consume_reports_the_precise_reason_and_changes_nothing(InvitationValidationResult expected)
    {
        var (id, code) = await CreateInvitationAsync(validForDays: 1);
        var author = await _api.SeedUserAsync(Email, UserRole.Author);
        var other = await _api.SeedUserAsync("someone.else@example.com", UserRole.Author);
        var email = Email;
        switch (expected)
        {
            case InvitationValidationResult.NotFound:
                code = "0000-0000-0000";
                break;
            case InvitationValidationResult.EmailMismatch:
                email = "wrong@example.com";
                break;
            case InvitationValidationResult.Expired:
                _api.Clock.Advance(TimeSpan.FromDays(1));
                break;
            case InvitationValidationResult.Revoked:
                await _api.ExecuteAsync("UPDATE invitations SET revoked_at = now(), revoked_by_user_id = @admin WHERE id = @id", ("admin", _admin.Id), ("id", id));
                break;
            case InvitationValidationResult.AlreadyUsed:
                await ConsumeAsync(code, Email, other.Id);
                break;
        }

        var ex = await Assert.ThrowsAsync<InvitationNotRedeemableException>(() => ConsumeAsync(code, email, author.Id));

        Assert.Equal(expected, ex.Reason);
        Assert.Equal(expected.Message(), ex.Message);
        var usedBy = await _api.ScalarAsync<object>("SELECT coalesce(used_by_user_id::text, '') FROM invitations WHERE id = @id", ("id", id));
        Assert.Equal(expected == InvitationValidationResult.AlreadyUsed ? other.Id.ToString() : string.Empty, usedBy);
    }

    // ---- endpoint ----

    private Task<HttpResponseMessage> PostAsync(object body) =>
        _api.SendJsonAsync(HttpMethod.Post, "/api/registration/validate-invitation", null, body);

    [Fact]
    public async Task The_endpoint_is_anonymous_returns_valid_true_and_does_not_consume_the_code()
    {
        var (id, code) = await CreateInvitationAsync();

        var first = await PostAsync(new { code = code.ToLowerInvariant(), email = Email });
        var second = await PostAsync(new { code, email = Email });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valid").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(await IsUsedAsync(id));
    }

    [Theory]
    [InlineData("NotFound", "This invitation code was not found.")]
    [InlineData("EmailMismatch", "This invitation was issued for a different email address.")]
    [InlineData("Expired", "This invitation has expired.")]
    [InlineData("Revoked", "This invitation has been revoked.")]
    [InlineData("AlreadyUsed", "This invitation has already been used.")]
    public async Task The_endpoint_answers_400_with_the_reason(string reason, string message)
    {
        var (id, code) = await CreateInvitationAsync(validForDays: 1);
        var author = await _api.SeedUserAsync("used.by@example.com", UserRole.Author);
        var email = Email;
        switch (reason)
        {
            case "NotFound":
                code = "0000-0000-0000";
                break;
            case "EmailMismatch":
                email = "wrong@example.com";
                break;
            case "Expired":
                _api.Clock.Advance(TimeSpan.FromDays(1));
                break;
            case "Revoked":
                await _api.ExecuteAsync("UPDATE invitations SET revoked_at = now(), revoked_by_user_id = @admin WHERE id = @id", ("admin", _admin.Id), ("id", id));
                break;
            case "AlreadyUsed":
                await ConsumeAsync(code, Email, author.Id);
                break;
        }

        var response = await PostAsync(new { code, email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(message, body.GetProperty("title").GetString());
        Assert.Equal(reason, body.GetProperty("reason").GetString());
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), body.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"code\":\"ABCD-EFGH-JKMN\"}")]
    [InlineData("{\"email\":\"a@example.com\"}")]
    [InlineData("{\"code\":\"\",\"email\":\"a@example.com\"}")]
    public async Task The_endpoint_requires_a_code_and_an_email(string json)
    {
        var response = await _api.Client.PostAsync(
            "/api/registration/validate-invitation", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task The_endpoint_is_rate_limited_like_login()
    {
        await using var limited = await ApiHost.CreateAsync(postgres, loginAttemptsPerMinute: 3);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await limited.SendJsonAsync(
                HttpMethod.Post, "/api/registration/validate-invitation", null, new { code = "0000-0000-0000", email = "a@example.com" })).StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            statuses);

        // The limiter is the login one: the same client is also held back from signing in.
        var login = await limited.LoginAsync("x@example.com", "whatever", "Admin");
        Assert.Equal(HttpStatusCode.TooManyRequests, login.StatusCode);
        Assert.True(login.Headers.Contains("Retry-After"));
    }
}
