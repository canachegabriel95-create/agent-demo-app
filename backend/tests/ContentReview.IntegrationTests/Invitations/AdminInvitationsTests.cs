using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Invitations;

[Collection(PostgresCollection.Name)]
public partial class AdminInvitationsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Route = "/api/admin/invitations";

    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private Domain.Entities.User _author = null!;
    private string _adminToken = null!;
    private string _authorToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _author = await _api.SeedUserAsync("author@demo.local", UserRole.Author, fullName: "Jane Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _authorToken = await _api.LoginTokenAsync("author@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$")]
    private static partial Regex CodeShape();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private Task<HttpResponseMessage> CreateRawAsync(object body) =>
        _api.SendJsonAsync(HttpMethod.Post, Route, _adminToken, body);

    private async Task<JsonElement> CreateAsync(string email, int? validForDays = null)
    {
        var response = await CreateRawAsync(validForDays is null ? new { email } : new { email, validForDays });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private Task<HttpResponseMessage> RevokeRawAsync(Guid id, object body) =>
        _api.SendJsonAsync(HttpMethod.Post, $"{Route}/{id}/revoke", _adminToken, body);

    private async Task<JsonElement> ListAsync(string query = "")
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}{query}", _adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    /// <summary>Moves the clock and signs in again: a session lives only 8 hours.</summary>
    private async Task AdvanceAsync(TimeSpan by)
    {
        _api.Clock.Advance(by);
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
    }

    private static string[] Emails(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("email").GetString()!).ToArray();

    // ---- create ----

    [Fact]
    public async Task Create_returns_201_with_a_code_creator_and_default_seven_day_expiry()
    {
        var response = await CreateRawAsync(new { email = "new.author@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.EndsWith($"{Route}/{id}", response.Headers.Location!.AbsolutePath);
        Assert.Matches(CodeShape(), body.GetProperty("code").GetString()!);
        Assert.Equal("new.author@example.com", body.GetProperty("email").GetString());
        Assert.Equal("Available", body.GetProperty("status").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(_api.Clock.GetUtcNow().AddDays(7), body.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(_admin.Id, body.GetProperty("createdBy").GetProperty("id").GetGuid());
        Assert.Equal("Alice Admin", body.GetProperty("createdBy").GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("revokedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("revokedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("usedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("usedBy").ValueKind);
        Assert.True(body.GetProperty("version").GetUInt32() > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    [InlineData(30)]
    public async Task Create_honours_validForDays(int days)
    {
        var body = await CreateAsync("days@example.com", days);

        Assert.Equal(_api.Clock.GetUtcNow().AddDays(days), body.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Create_trims_the_email_and_stores_the_normalized_form()
    {
        var body = await CreateAsync("  Mixed.Case@Example.COM ");

        Assert.Equal("Mixed.Case@Example.COM", body.GetProperty("email").GetString());
        var stored = await _api.ScalarAsync<string>(
            "SELECT normalized_email FROM invitations WHERE id = @id", ("id", body.GetProperty("id").GetGuid()));
        Assert.Equal("mixed.case@example.com", stored);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    [InlineData(365)]
    public async Task Create_rejects_validity_outside_1_to_30_days(int days)
    {
        var response = await CreateRawAsync(new { email = "a@example.com", validForDays = days });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.Contains("1 and 30", errors.GetProperty("validForDays")[0].GetString());
        Assert.Equal(0, await _api.ScalarAsync<long>("SELECT count(*) FROM invitations"));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("Name <a@example.com>")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_an_invalid_email(string email)
    {
        var response = await CreateRawAsync(new { email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Create_rejects_a_missing_or_too_long_email_and_reports_every_field()
    {
        var missing = await CreateRawAsync(new { validForDays = 5 });
        var tooLong = await CreateRawAsync(new { email = new string('a', 250) + "@example.com" });
        var both = await CreateRawAsync(new { email = "bad", validForDays = 99 });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True((await ReadAsync(missing)).GetProperty("errors").TryGetProperty("email", out _));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var errors = (await ReadAsync(both)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("validForDays", out _));
    }

    [Fact]
    public async Task Create_is_rejected_with_409_when_a_user_already_has_the_email()
    {
        var response = await CreateRawAsync(new { email = "  AUTHOR@demo.local " });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("A user with this email already exists.", body.GetProperty("detail").GetString());
        Assert.Equal(0, await _api.ScalarAsync<long>("SELECT count(*) FROM invitations"));
        Assert.Empty(_api.Audit.Events);
    }

    [Fact]
    public async Task Several_available_invitations_for_the_same_email_are_allowed_with_different_codes()
    {
        var first = await CreateAsync("same@example.com");
        var second = await CreateAsync("SAME@example.com");

        Assert.NotEqual(first.GetProperty("code").GetString(), second.GetProperty("code").GetString());
        Assert.Equal(2, (await ListAsync("?email=same@example.com")).GetProperty("totalCount").GetInt32());
    }

    // ---- authorization ----

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "")]
    [InlineData("POST", "/00000000-0000-0000-0000-000000000001/revoke")]
    public async Task Only_administrators_may_use_the_endpoints(string method, string path)
    {
        var http = new HttpMethod(method);
        object? body = method == "POST" ? new { email = "x@example.com", version = 1 } : null;

        var anonymous = await _api.SendJsonAsync(http, Route + path, null, body);
        var author = await _api.SendJsonAsync(http, Route + path, _authorToken, body);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, author.StatusCode);
        Assert.Equal(0, await _api.ScalarAsync<long>("SELECT count(*) FROM invitations"));
    }

    // ---- get ----

    [Fact]
    public async Task Get_returns_the_invitation_including_the_full_code()
    {
        var created = await CreateAsync("get@example.com");
        var id = created.GetProperty("id").GetGuid();

        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}/{id}", _adminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(created.GetProperty("code").GetString(), body.GetProperty("code").GetString());
        Assert.Equal(created.GetProperty("version").GetUInt32(), body.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task Get_of_an_unknown_id_is_404()
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}/{Guid.NewGuid()}", _adminToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    // ---- list, filters and paging ----

    /// <summary>One invitation per status. Returns the ids keyed by the email's local part.</summary>
    private async Task<Dictionary<string, Guid>> SeedOneOfEachStatusAsync()
    {
        var ids = new Dictionary<string, Guid>();
        foreach (var name in new[] { "expired", "used", "revoked", "available" })
        {
            var body = await CreateAsync($"{name}@example.com", name == "expired" ? 1 : null);
            ids[name] = body.GetProperty("id").GetGuid();
            await AdvanceAsync(TimeSpan.FromMinutes(1));
        }

        await _api.ExecuteAsync(
            "UPDATE invitations SET used_at = @at, used_by_user_id = @user WHERE id = @id",
            ("at", _api.Clock.GetUtcNow()), ("user", _author.Id), ("id", ids["used"]));
        var current = (await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"{Route}/{ids["revoked"]}", _adminToken)))
            .GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.OK, (await RevokeRawAsync(ids["revoked"], new { version = current })).StatusCode);

        await AdvanceAsync(TimeSpan.FromDays(2)); // only the one-day invitation has run out
        return ids;
    }

    [Theory]
    [InlineData("Available", "available")]
    [InlineData("Used", "used")]
    [InlineData("Revoked", "revoked")]
    [InlineData("Expired", "expired")]
    public async Task List_filters_by_status_in_the_database(string status, string expectedLocalPart)
    {
        await SeedOneOfEachStatusAsync();

        var page = await ListAsync($"?status={status}");

        Assert.Equal([$"{expectedLocalPart}@example.com"], Emails(page));
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(status, page.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_without_filters_returns_everything_newest_first_with_page_defaults()
    {
        await SeedOneOfEachStatusAsync();

        var page = await ListAsync();

        Assert.Equal(["available@example.com", "revoked@example.com", "used@example.com", "expired@example.com"], Emails(page));
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task An_invitation_expires_exactly_at_expiresAt()
    {
        var body = await CreateAsync("edge@example.com", 1);
        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();

        await AdvanceAsync(expiresAt.AddTicks(-1) - _api.Clock.GetUtcNow());
        Assert.Equal(["edge@example.com"], Emails(await ListAsync("?status=Available")));
        Assert.Empty(Emails(await ListAsync("?status=Expired")));

        await AdvanceAsync(expiresAt - _api.Clock.GetUtcNow());
        Assert.Empty(Emails(await ListAsync("?status=Available")));
        Assert.Equal(["edge@example.com"], Emails(await ListAsync("?status=Expired")));
    }

    [Fact]
    public async Task A_used_or_revoked_invitation_is_not_listed_as_expired_after_its_expiry()
    {
        var ids = await SeedOneOfEachStatusAsync();
        await AdvanceAsync(TimeSpan.FromDays(60));

        Assert.Equal(["used@example.com"], Emails(await ListAsync("?status=Used")));
        Assert.Equal(["revoked@example.com"], Emails(await ListAsync("?status=Revoked")));
        Assert.Equal(
            ["available@example.com", "expired@example.com"],
            Emails(await ListAsync("?status=Expired")).Order().ToArray());
        Assert.Equal(4, ids.Count);
    }

    [Theory]
    [InlineData("?email=EXPIRED", "expired@example.com")]
    [InlineData("?email=ed@exa", "revoked@example.com|used@example.com|expired@example.com")]
    [InlineData("?email=%20Used%20", "used@example.com")]
    [InlineData("?email=nobody", "")]
    public async Task List_filters_by_part_of_the_email_ignoring_case(string query, string expected)
    {
        await SeedOneOfEachStatusAsync();

        var emails = Emails(await ListAsync(query));

        Assert.Equal(expected.Length == 0 ? [] : expected.Split('|'), emails);
    }

    [Fact]
    public async Task List_combines_the_email_and_status_filters()
    {
        await SeedOneOfEachStatusAsync();

        Assert.Equal(["used@example.com"], Emails(await ListAsync("?email=used&status=Used")));
        Assert.Empty(Emails(await ListAsync("?email=used&status=Available")));
    }

    [Fact]
    public async Task List_treats_like_wildcards_in_the_email_filter_literally()
    {
        await CreateAsync("plain@example.com");

        Assert.Empty(Emails(await ListAsync("?email=%25")));
        Assert.Empty(Emails(await ListAsync("?email=_____")));
    }

    [Fact]
    public async Task List_pages_the_results()
    {
        for (var i = 1; i <= 5; i++)
        {
            await CreateAsync($"user{i}@example.com");
            await AdvanceAsync(TimeSpan.FromSeconds(1));
        }

        var second = await ListAsync("?page=2&pageSize=2");
        var third = await ListAsync("?page=3&pageSize=2");
        var beyond = await ListAsync("?page=9&pageSize=2");

        Assert.Equal(["user3@example.com", "user2@example.com"], Emails(second));
        Assert.Equal(5, second.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(2, second.GetProperty("pageSize").GetInt32());
        Assert.Equal(["user1@example.com"], Emails(third));
        Assert.Empty(Emails(beyond));
        Assert.Equal(5, beyond.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=abc")]
    [InlineData("?status=Bogus")]
    [InlineData("?status=99")]
    public async Task List_rejects_invalid_paging_and_status_values(string query)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}{query}", _adminToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task List_accepts_the_maximum_page_size()
    {
        var page = await ListAsync("?pageSize=100");

        Assert.Equal(100, page.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task A_used_invitation_names_the_user_who_used_it()
    {
        var ids = await SeedOneOfEachStatusAsync();

        var body = await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"{Route}/{ids["used"]}", _adminToken));

        Assert.Equal("Used", body.GetProperty("status").GetString());
        Assert.Equal(_author.Id, body.GetProperty("usedBy").GetProperty("id").GetGuid());
        Assert.Equal("Jane Author", body.GetProperty("usedBy").GetProperty("fullName").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("usedAt").ValueKind);
    }

    // ---- revoke ----

    [Fact]
    public async Task Revoke_marks_an_available_invitation_as_revoked()
    {
        var created = await CreateAsync("revoke@example.com");
        var id = created.GetProperty("id").GetGuid();
        await AdvanceAsync(TimeSpan.FromHours(1));

        var response = await RevokeRawAsync(id, new { version = created.GetProperty("version").GetUInt32() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Revoked", body.GetProperty("status").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), body.GetProperty("revokedAt").GetDateTimeOffset());
        Assert.Equal(_admin.Id, body.GetProperty("revokedBy").GetProperty("id").GetGuid());
        Assert.Equal("Alice Admin", body.GetProperty("revokedBy").GetProperty("fullName").GetString());
        Assert.NotEqual(created.GetProperty("version").GetUInt32(), body.GetProperty("version").GetUInt32());
        Assert.Equal(1, await _api.ScalarAsync<long>(
            "SELECT count(*) FROM invitations WHERE id = @id AND revoked_at IS NOT NULL AND revoked_by_user_id = @admin",
            ("id", id), ("admin", _admin.Id)));
    }

    [Fact]
    public async Task Revoke_with_a_stale_version_is_409_and_changes_nothing()
    {
        var created = await CreateAsync("stale@example.com");
        var id = created.GetProperty("id").GetGuid();
        var staleVersion = created.GetProperty("version").GetUInt32();
        await _api.ExecuteAsync("UPDATE invitations SET expires_at = expires_at + interval '1 day' WHERE id = @id", ("id", id));

        var response = await RevokeRawAsync(id, new { version = staleVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await _api.ScalarAsync<long>("SELECT count(*) FROM invitations WHERE revoked_at IS NOT NULL"));
        Assert.DoesNotContain(_api.Audit.Events, e => e.Action == "InvitationRevoked");
    }

    [Theory]
    [InlineData("used")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task Revoke_of_an_invitation_that_is_not_available_is_409(string status)
    {
        var ids = await SeedOneOfEachStatusAsync();
        var id = ids[status];
        var current = (await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"{Route}/{id}", _adminToken)))
            .GetProperty("version").GetUInt32();
        var before = await _api.ScalarAsync<long>("SELECT count(*) FROM invitations WHERE revoked_at IS NOT NULL");

        var response = await RevokeRawAsync(id, new { version = current });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Only an available invitation can be revoked", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Equal(before, await _api.ScalarAsync<long>("SELECT count(*) FROM invitations WHERE revoked_at IS NOT NULL"));
    }

    [Fact]
    public async Task Revoke_of_an_unknown_id_is_404_and_a_missing_version_is_400()
    {
        var created = await CreateAsync("v@example.com");

        var unknown = await RevokeRawAsync(Guid.NewGuid(), new { version = 1 });
        var missing = await RevokeRawAsync(created.GetProperty("id").GetGuid(), new { });

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True((await ReadAsync(missing)).GetProperty("errors").TryGetProperty("version", out _));
    }

    [Fact]
    public async Task Two_simultaneous_revokes_of_the_same_version_let_exactly_one_win()
    {
        var created = await CreateAsync("race@example.com");
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetUInt32();

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => RevokeRawAsync(id, new { version })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // The recorder keeps what was asked for, even by the request whose save then failed; the database is the truth.
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM invitations WHERE id = @id AND revoked_at IS NOT NULL", ("id", id)));
    }

    // ---- audit ----

    [Fact]
    public async Task Audit_events_carry_only_the_last_four_characters_of_the_code()
    {
        var created = await CreateAsync("audit@example.com");
        var id = created.GetProperty("id").GetGuid();
        var code = created.GetProperty("code").GetString()!;
        await RevokeRawAsync(id, new { version = created.GetProperty("version").GetUInt32() });

        var events = _api.Audit.Events;

        Assert.Equal(["InvitationCreated", "InvitationRevoked"], events.Select(e => e.Action));
        Assert.All(events, e =>
        {
            Assert.Equal("Invitation", e.EntityType);
            Assert.Equal(id, e.EntityId);
            Assert.Equal(_admin.Id, e.ActorUserId);
            var details = JsonDocument.Parse(e.DetailsJson).RootElement;
            Assert.Equal("audit@example.com", details.GetProperty("email").GetString());
            Assert.Equal(code[^4..], details.GetProperty("codeSuffix").GetString());
            Assert.DoesNotContain(code, e.DetailsJson);
            Assert.DoesNotContain(code.Replace("-", string.Empty), e.DetailsJson);
            Assert.DoesNotContain(code[..9], e.DetailsJson);
        });
    }

    [Fact]
    public async Task Codes_are_not_written_to_the_logs()
    {
        var created = await CreateAsync("logs@example.com");
        var code = created.GetProperty("code").GetString()!;
        await _api.SendAsync(HttpMethod.Get, $"{Route}/{created.GetProperty("id").GetGuid()}", _adminToken);

        Assert.DoesNotContain(_api.Logs.Entries, log => log.Everything.Contains(code) || log.Everything.Contains(code.Replace("-", string.Empty)));
    }

    [Fact]
    public async Task The_code_column_has_a_unique_index_that_backs_the_collision_retry()
    {
        var created = await CreateAsync("unique@example.com");

        var indexed = await _api.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'invitations' AND indexdef ILIKE '%UNIQUE%(code)%')");

        Assert.True(indexed);
        Assert.Matches(CodeShape(), created.GetProperty("code").GetString()!);
        Assert.Equal(InvitationCode.Length, created.GetProperty("code").GetString()!.Replace("-", string.Empty).Length);
    }
}
