using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Registration;

[Collection(PostgresCollection.Name)]
public class RegistrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Route = "/api/registration";
    private const string Email = "jane@demo.local";
    private const string StrongPassword = "Str0ng!Passw0rd#9";

    private const string NotFoundMessage = "This invitation code is not valid. Check the code or ask an administrator for a new one.";
    private const string ExpiredMessage = "This invitation has expired. Ask an administrator for a new one.";
    private const string RevokedMessage = "This invitation has been revoked. Ask an administrator for a new one.";
    private const string UsedMessage = "This invitation has already been used.";
    private const string MismatchMessage = "The email address does not match this invitation.";

    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private string _adminToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(Guid Id, string Code)> InviteAsync(string email = Email, int? days = null)
    {
        var response = await _api.SendJsonAsync(HttpMethod.Post, "/api/admin/invitations", _adminToken, new { email, validForDays = days });
        response.EnsureSuccessStatusCode();
        var body = await ReadAsync(response);
        return (body.GetProperty("id").GetGuid(), body.GetProperty("code").GetString()!);
    }

    private Task<HttpResponseMessage> RegisterAsync(
        string? code, string? email = Email, string? fullName = "Jane Doe", string? password = StrongPassword, string? confirm = null) =>
        _api.SendJsonAsync(HttpMethod.Post, Route, null, new { invitationCode = code, fullName, email, password, confirmPassword = confirm ?? password });

    private Task<long> UserCountAsync(string normalizedEmail) =>
        _api.ScalarAsync<long>("SELECT count(*) FROM users WHERE normalized_email = @e", ("e", normalizedEmail));

    private Task<bool> IsUsedAsync(Guid invitationId) =>
        _api.ScalarAsync<bool>("SELECT used_at IS NOT NULL OR used_by_user_id IS NOT NULL FROM invitations WHERE id = @id", ("id", invitationId));

    private async Task AssertNothingChangedAsync(Guid invitationId, string email = Email)
    {
        Assert.Equal(0, await UserCountAsync(EmailNormalized(email)));
        Assert.False(await IsUsedAsync(invitationId));
    }

    private static string EmailNormalized(string email) => email.Trim().ToLowerInvariant();

    private static string[] Errors(JsonElement body, string field) =>
        body.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    // ---- success ----

    [Fact]
    public async Task A_valid_registration_creates_an_active_author_and_uses_the_invitation()
    {
        var (invitationId, code) = await InviteAsync();

        var response = await RegisterAsync(code);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response);
        var userId = body.GetProperty("id").GetGuid();
        Assert.Equal(Email, body.GetProperty("email").GetString());
        Assert.Equal("Jane Doe", body.GetProperty("fullName").GetString());
        Assert.Equal(3, body.EnumerateObject().Count());

        Assert.Equal("Author", await _api.ScalarAsync<string>("SELECT role FROM users WHERE id = @id", ("id", userId)));
        Assert.Equal("Active", await _api.ScalarAsync<string>("SELECT status FROM users WHERE id = @id", ("id", userId)));
        Assert.Equal(userId, await _api.ScalarAsync<Guid>("SELECT used_by_user_id FROM invitations WHERE id = @id", ("id", invitationId)));
        Assert.Equal(_api.Clock.GetUtcNow(), new DateTimeOffset(await _api.ScalarAsync<DateTime>(
            "SELECT used_at FROM invitations WHERE id = @id", ("id", invitationId)), TimeSpan.Zero));
    }

    [Fact]
    public async Task The_password_is_stored_only_as_a_hash_and_the_new_author_can_sign_in_to_the_author_portal_only()
    {
        var (_, code) = await InviteAsync();
        await RegisterAsync(code);

        var stored = await _api.ScalarAsync<string>("SELECT password_hash FROM users WHERE normalized_email = @e", ("e", Email));
        Assert.DoesNotContain(StrongPassword, stored);
        Assert.StartsWith("AQAAAA", stored); // ASP.NET Identity V3 format

        Assert.Equal(HttpStatusCode.OK, (await _api.LoginAsync(Email, StrongPassword, "Author")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.LoginAsync(Email, StrongPassword, "Admin")).StatusCode);
    }

    [Fact]
    public async Task Registration_creates_no_session_and_returns_no_token()
    {
        var (_, code) = await InviteAsync();

        var response = await RegisterAsync(code);

        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _api.ScalarAsync<long>(
            "SELECT count(*) FROM user_sessions s JOIN users u ON u.id = s.user_id WHERE u.normalized_email = @e", ("e", Email)));
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("administrator")]
    [InlineData(0)]
    public async Task A_role_in_the_request_is_ignored_and_the_account_is_always_an_author(object role)
    {
        var (_, code) = await InviteAsync();

        var response = await _api.SendJsonAsync(
            HttpMethod.Post, Route, null,
            new { invitationCode = code, fullName = "Jane Doe", email = Email, password = StrongPassword, confirmPassword = StrongPassword, role });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Author", await _api.ScalarAsync<string>("SELECT role FROM users WHERE normalized_email = @e", ("e", Email)));
    }

    [Fact]
    public async Task The_code_and_email_are_matched_ignoring_case_dashes_and_surrounding_spaces()
    {
        var (invitationId, code) = await InviteAsync("jane@demo.local");

        var response = await RegisterAsync($"  {code.Replace("-", string.Empty).ToLowerInvariant()} ", "  JANE@Demo.LOCAL ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("JANE@Demo.LOCAL", (await ReadAsync(response)).GetProperty("email").GetString());
        Assert.Equal("jane@demo.local", await _api.ScalarAsync<string>(
            "SELECT normalized_email FROM users WHERE normalized_email = 'jane@demo.local'"));
        Assert.True(await IsUsedAsync(invitationId));
    }

    [Fact]
    public async Task The_full_name_is_trimmed()
    {
        var (_, code) = await InviteAsync();

        var response = await RegisterAsync(code, fullName: "   Jane Doe  ");

        Assert.Equal("Jane Doe", (await ReadAsync(response)).GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task A_UserRegistered_audit_event_names_the_user_email_and_invitation_without_secrets()
    {
        var (invitationId, code) = await InviteAsync();
        var response = await RegisterAsync(code);
        var userId = (await ReadAsync(response)).GetProperty("id").GetGuid();

        var audit = Assert.Single(_api.Audit.Events, e => e.Action == "UserRegistered");

        Assert.Equal("User", audit.EntityType);
        Assert.Equal(userId, audit.EntityId);
        Assert.Equal(userId, audit.ActorUserId);
        var details = JsonDocument.Parse(audit.DetailsJson).RootElement;
        Assert.Equal(userId, details.GetProperty("userId").GetGuid());
        Assert.Equal(Email, details.GetProperty("email").GetString());
        Assert.Equal(invitationId, details.GetProperty("invitationId").GetGuid());
        Assert.DoesNotContain(StrongPassword, audit.DetailsJson);
        Assert.DoesNotContain(code, audit.DetailsJson);
        Assert.DoesNotContain(code.Replace("-", string.Empty), audit.DetailsJson);
        Assert.DoesNotContain("password", audit.DetailsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Passwords_and_codes_are_not_written_to_the_logs()
    {
        var (_, code) = await InviteAsync();
        await RegisterAsync(code);
        await RegisterAsync(code, password: "Wr0ng!ButLogged#1");

        Assert.DoesNotContain(_api.Logs.Entries, l => l.Everything.Contains(StrongPassword) || l.Everything.Contains("Wr0ng!ButLogged#1"));
        Assert.DoesNotContain(_api.Logs.Entries, l => l.Everything.Contains(code));
    }

    // ---- field validation ----

    [Fact]
    public async Task Missing_values_are_reported_for_every_field_at_once()
    {
        var response = await _api.SendJsonAsync(HttpMethod.Post, Route, null, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(
            ["confirmPassword", "email", "fullName", "invitationCode", "password"],
            body.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(["Invitation code is required."], Errors(body, "invitationCode"));
        Assert.Equal(["Full name is required."], Errors(body, "fullName"));
        Assert.Equal(["Email is required."], Errors(body, "email"));
        Assert.Equal(["Password is required."], Errors(body, "password"));
        Assert.Equal(["Confirm your password."], Errors(body, "confirmPassword"));
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), body.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("J", "Full name must be between 2 and 100 characters.")]
    [InlineData("   J   ", "Full name must be between 2 and 100 characters.")]
    [InlineData("   ", "Full name is required.")]
    public async Task The_full_name_must_be_2_to_100_characters_after_trimming(string name, string message)
    {
        var (invitationId, code) = await InviteAsync();

        var response = await RegisterAsync(code, fullName: name);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], Errors(await ReadAsync(response), "fullName"));
        await AssertNothingChangedAsync(invitationId);
    }

    [Fact]
    public async Task A_hundred_character_name_is_accepted_and_a_hundred_and_one_is_not()
    {
        var (_, first) = await InviteAsync("a@demo.local");
        var (_, second) = await InviteAsync("b@demo.local");

        var ok = await RegisterAsync(first, "a@demo.local", new string('n', 100));
        var tooLong = await RegisterAsync(second, "b@demo.local", new string('n', 101));

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("Jane <jane@demo.local>")]
    [InlineData("jane@localhost")]
    public async Task The_email_must_be_valid(string email)
    {
        var (invitationId, code) = await InviteAsync();

        var response = await RegisterAsync(code, email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Enter a valid email address of at most 254 characters."], Errors(await ReadAsync(response), "email"));
        await AssertNothingChangedAsync(invitationId);
    }

    [Fact]
    public async Task A_weak_password_lists_every_rule_it_breaks()
    {
        var (invitationId, code) = await InviteAsync();

        var response = await RegisterAsync(code, password: "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            [
                "Must be between 12 and 128 characters long.",
                "Must contain at least one uppercase letter.",
                "Must contain at least one digit.",
                "Must contain at least one symbol (a character that is not a letter, a digit or white space).",
            ],
            Errors(await ReadAsync(response), "password"));
        await AssertNothingChangedAsync(invitationId);
    }

    [Theory]
    [InlineData("str0ng!passw0rd#9", "Must contain at least one uppercase letter.")]
    [InlineData("STR0NG!PASSW0RD#9", "Must contain at least one lowercase letter.")]
    [InlineData("Strong!Password#x", "Must contain at least one digit.")]
    [InlineData("Str0ng Passw0rd 9", "Must contain at least one symbol (a character that is not a letter, a digit or white space).")]
    public async Task Each_password_rule_is_enforced_by_the_api(string password, string message)
    {
        var (_, code) = await InviteAsync();

        var response = await RegisterAsync(code, password: password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], Errors(await ReadAsync(response), "password"));
    }

    [Fact]
    public async Task A_129_character_password_is_rejected_without_being_hashed()
    {
        var (_, code) = await InviteAsync();

        var response = await RegisterAsync(code, password: "Aa1!" + new string('x', 125));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Must be between 12 and 128 characters long."], Errors(await ReadAsync(response), "password"));
    }

    [Fact]
    public async Task The_confirmation_must_match_the_password_exactly()
    {
        var (invitationId, code) = await InviteAsync();

        var response = await RegisterAsync(code, confirm: StrongPassword + " ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Passwords do not match."], Errors(await ReadAsync(response), "confirmPassword"));
        await AssertNothingChangedAsync(invitationId);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_400()
    {
        var response = await _api.Client.PostAsync(Route, new StringContent("{ nope", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    // ---- invitation failures ----

    [Fact]
    public async Task An_unknown_or_malformed_code_is_not_valid()
    {
        var (invitationId, _) = await InviteAsync();

        foreach (var code in new[] { "0000-0000-0000", "garbage", "ABCD" })
        {
            var response = await RegisterAsync(code);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal([NotFoundMessage], Errors(await ReadAsync(response), "invitationCode"));
        }

        await AssertNothingChangedAsync(invitationId);
    }

    [Fact]
    public async Task An_expired_invitation_is_reported_on_the_code_field()
    {
        var (invitationId, code) = await InviteAsync(days: 1);
        _api.Clock.Advance(TimeSpan.FromDays(1));

        var response = await RegisterAsync(code);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([ExpiredMessage], Errors(await ReadAsync(response), "invitationCode"));
        await AssertNothingChangedAsync(invitationId);
    }

    [Fact]
    public async Task A_revoked_invitation_is_reported_on_the_code_field()
    {
        var (invitationId, code) = await InviteAsync();
        await _api.ExecuteAsync(
            "UPDATE invitations SET revoked_at = now(), revoked_by_user_id = @admin WHERE id = @id", ("admin", _admin.Id), ("id", invitationId));

        var response = await RegisterAsync(code);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([RevokedMessage], Errors(await ReadAsync(response), "invitationCode"));
        await AssertNothingChangedAsync(invitationId);
    }

    [Fact]
    public async Task A_used_invitation_cannot_be_used_again()
    {
        var (_, code) = await InviteAsync();
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(code)).StatusCode);

        var second = await RegisterAsync(code, fullName: "Someone Else");

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal([UsedMessage], Errors(await ReadAsync(second), "invitationCode"));
        Assert.Equal(1, await UserCountAsync(Email));
    }

    [Fact]
    public async Task Another_email_is_reported_on_the_email_field()
    {
        var (invitationId, code) = await InviteAsync();

        var response = await RegisterAsync(code, "someone.else@demo.local");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([MismatchMessage], Errors(await ReadAsync(response), "email"));
        await AssertNothingChangedAsync(invitationId);
        await AssertNothingChangedAsync(invitationId, "someone.else@demo.local");
    }

    // ---- existing accounts ----

    private async Task<(Guid Id, string Code)> InsertInvitationForExistingUserAsync(string email)
    {
        var id = Guid.CreateVersion7();
        var code = "ABCD-EFGH-JKMN";
        await _api.ExecuteAsync(
            "INSERT INTO invitations (id, code, email, normalized_email, created_by_user_id, created_at, expires_at) " +
            "VALUES (@id, @code, @email, @normalized, @admin, now(), now() + interval '7 days')",
            ("id", id), ("code", code), ("email", email), ("normalized", email.ToLowerInvariant()), ("admin", _admin.Id));
        return (id, code);
    }

    [Theory]
    [InlineData("taken@demo.local", "taken@demo.local")]
    [InlineData("taken@demo.local", "TAKEN@Demo.Local")]
    public async Task An_email_that_already_has_an_account_is_a_409_and_the_invitation_stays_available(string existing, string typed)
    {
        await _api.SeedUserAsync(existing, UserRole.Author);
        var (invitationId, code) = await InsertInvitationForExistingUserAsync(existing);

        var response = await RegisterAsync(code, typed);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("An account with this email already exists.", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.False(await IsUsedAsync(invitationId));
        Assert.Equal(1, await UserCountAsync("taken@demo.local"));
    }

    [Fact]
    public async Task An_existing_email_is_not_revealed_to_someone_without_a_valid_invitation_for_it()
    {
        await _api.SeedUserAsync("taken@demo.local", UserRole.Author);

        var response = await RegisterAsync("0000-0000-0000", "taken@demo.local");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([NotFoundMessage], Errors(await ReadAsync(response), "invitationCode"));
    }

    // ---- atomicity ----

    [Fact]
    public async Task A_failure_after_the_user_was_inserted_leaves_no_user_and_the_invitation_available()
    {
        var (invitationId, code) = await InviteAsync();
        _api.Audit.FailOn = "UserRegistered"; // the last step, after the insert and the consumption

        var failed = await RegisterAsync(code);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await AssertNothingChangedAsync(invitationId);
        Assert.DoesNotContain("Simulated", await failed.Content.ReadAsStringAsync());

        // The invitation really is still usable.
        _api.Audit.FailOn = null;
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(code)).StatusCode);
        Assert.True(await IsUsedAsync(invitationId));
    }

    [Fact]
    public async Task Parallel_registrations_with_the_same_code_let_exactly_one_succeed()
    {
        var (invitationId, code) = await InviteAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RegisterAsync(code)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var others = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        Assert.Equal(7, others.Count);
        Assert.All(others, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        foreach (var response in others)
        {
            Assert.Equal([UsedMessage], Errors(await ReadAsync(response), "invitationCode"));
        }

        Assert.Equal(1, await UserCountAsync(Email));
        Assert.True(await IsUsedAsync(invitationId));
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM invitations WHERE used_by_user_id IS NOT NULL"));
    }

    // ---- limits ----

    [Fact]
    public async Task Registration_is_rate_limited_like_login()
    {
        await using var limited = await ApiHost.CreateAsync(postgres, loginAttemptsPerMinute: 2);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await limited.SendJsonAsync(HttpMethod.Post, Route, null, new { })).StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            statuses);
    }

    [Fact]
    public async Task Registration_needs_no_session_and_ignores_one()
    {
        var (_, code) = await InviteAsync();

        var response = await _api.SendJsonAsync(
            HttpMethod.Post, Route, _adminToken,
            new { invitationCode = code, fullName = "Jane Doe", email = Email, password = StrongPassword, confirmPassword = StrongPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
