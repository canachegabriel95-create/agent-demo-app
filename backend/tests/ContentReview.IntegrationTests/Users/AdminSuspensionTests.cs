using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Users;

[Collection(PostgresCollection.Name)]
public class AdminSuspensionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Reason = "Posted spam links repeatedly.";

    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private Domain.Entities.User _author = null!;
    private string _adminToken = null!;
    private string _authorToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _author = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _authorToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<uint> VersionAsync(Guid? id = null) =>
        (await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"/api/admin/users/{id ?? _author.Id}", _adminToken)))
        .GetProperty("version").GetUInt32();

    private Task<HttpResponseMessage> PostAsync(string action, object body, Guid? id = null, string? token = null) =>
        _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/users/{id ?? _author.Id}/{action}", token ?? _adminToken, body);

    private async Task<HttpResponseMessage> SuspendAsync(string? reason = Reason) =>
        await PostAsync("suspend", new { reason, version = await VersionAsync() });

    private async Task<HttpResponseMessage> ReactivateAsync() =>
        await PostAsync("reactivate", new { version = await VersionAsync() });

    private Task<string> ColumnAsync(string column, Guid? id = null) =>
        _api.ScalarAsync<string>($"SELECT coalesce({column}::text, '<null>') FROM users WHERE id = @id", ("id", id ?? _author.Id));

    private Task<long> ActiveSessionsAsync(Guid? userId = null) =>
        _api.ScalarAsync<long>(
            "SELECT count(*) FROM user_sessions WHERE user_id = @id AND revoked_at IS NULL", ("id", userId ?? _author.Id));

    private async Task<HttpStatusCode> MeAsync(string token) =>
        (await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token)).StatusCode;

    private async Task AddArticleAsync(Guid authorId, string status) =>
        await _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, title, summary, body, status, submission_version, created_at, updated_at) " +
            "VALUES (@id, @author, 'Title', 'Summary', 'Body', @status, 1, now(), now())",
            ("id", Guid.CreateVersion7()), ("author", authorId), ("status", status));

    private Task<string> ArticleSnapshotAsync() =>
        _api.ScalarAsync<string>(
            "SELECT coalesce(string_agg(id::text || status || updated_at::text || xmin::text, ',' ORDER BY id), '') " +
            "FROM articles WHERE author_id = @id", ("id", _author.Id));

    // ---- suspend ----

    [Fact]
    public async Task Suspending_stores_status_reason_and_time_and_returns_the_details()
    {
        _api.Clock.Advance(TimeSpan.FromHours(2));

        var response = await SuspendAsync("  " + Reason + "  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Suspended", body.GetProperty("status").GetString());
        Assert.Equal(Reason, body.GetProperty("suspensionReason").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), body.GetProperty("suspendedAt").GetDateTimeOffset());
        Assert.Equal("Suspended", await ColumnAsync("status"));
        Assert.Equal(Reason, await ColumnAsync("suspension_reason"));
    }

    [Fact]
    public async Task All_sessions_of_the_author_are_revoked_and_other_users_keep_theirs()
    {
        await _api.LoginTokenAsync("ann@demo.local", "Author");
        await _api.LoginTokenAsync("ann@demo.local", "Author");
        Assert.Equal(3, await ActiveSessionsAsync());

        await SuspendAsync();

        Assert.Equal(0, await ActiveSessionsAsync());
        Assert.Equal(1, await ActiveSessionsAsync(_admin.Id));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(_adminToken));
    }

    [Fact]
    public async Task The_existing_token_of_a_suspended_author_gets_401_on_the_next_call()
    {
        Assert.Equal(HttpStatusCode.OK, await MeAsync(_authorToken));

        await SuspendAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(_authorToken));
    }

    [Fact]
    public async Task A_suspended_author_cannot_sign_in_and_gets_the_generic_401()
    {
        await SuspendAsync();

        var suspended = await _api.LoginAsync("ann@demo.local", ApiHost.Password, "Author");
        var wrongPassword = await _api.LoginAsync("ann@demo.local", "Wrong!Passw0rd#1", "Author");

        Assert.Equal(HttpStatusCode.Unauthorized, suspended.StatusCode);
        var suspendedShape = await ShapeAsync(suspended);
        Assert.Equal(await ShapeAsync(wrongPassword), suspendedShape);
        Assert.Contains("Invalid email or password.", suspendedShape);
    }

    /// <summary>Everything in a problem body except the per-request correlation id.</summary>
    private static async Task<string> ShapeAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        return string.Join("|", body.EnumerateObject()
            .Where(p => p.Name != "correlationId" && p.Name != "traceId")
            .Select(p => $"{p.Name}={p.Value}"));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    public async Task The_reason_limits_are_inclusive(int length)
    {
        var response = await SuspendAsync(new string('r', length));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(length, (await ColumnAsync("suspension_reason")).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    public async Task A_missing_reason_is_a_400_and_nothing_changes(string? reason)
    {
        var response = await SuspendAsync(reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Reason is required.", (await ReadAsync(response)).GetProperty("errors").GetProperty("reason")[0].GetString());
        await AssertUntouchedAsync();
    }

    [Theory]
    [InlineData(9)]
    [InlineData(501)]
    public async Task A_reason_outside_10_to_500_characters_is_a_400_and_nothing_changes(int length)
    {
        var response = await SuspendAsync(new string('r', length));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Reason must be between 10 and 500 characters.",
            (await ReadAsync(response)).GetProperty("errors").GetProperty("reason")[0].GetString());
        await AssertUntouchedAsync();
    }

    [Fact]
    public async Task A_missing_version_is_a_400()
    {
        var response = await PostAsync("suspend", new { reason = Reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("version", out _));
        await AssertUntouchedAsync();
    }

    private async Task AssertUntouchedAsync()
    {
        Assert.Equal("Active", await ColumnAsync("status"));
        Assert.Equal("<null>", await ColumnAsync("suspension_reason"));
        Assert.Equal("<null>", await ColumnAsync("suspended_at"));
        Assert.Equal(1, await ActiveSessionsAsync());
        Assert.Empty(_api.Audit.Events);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(_authorToken));
    }

    [Fact]
    public async Task An_administrator_cannot_be_suspended_it_is_a_400_and_the_admin_keeps_working()
    {
        var response = await PostAsync("suspend", new { reason = Reason, version = 1 }, _admin.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Administrator accounts cannot be suspended.",
            (await ReadAsync(response)).GetProperty("errors").GetProperty("id")[0].GetString());
        Assert.Equal("Active", await ColumnAsync("status", _admin.Id));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(_adminToken));
        Assert.Empty(_api.Audit.Events);
    }

    [Fact]
    public async Task An_administrator_cannot_suspend_themselves_either()
    {
        var response = await PostAsync("suspend", new { reason = Reason, version = 1 }, _admin.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await ActiveSessionsAsync(_admin.Id));
    }

    [Fact]
    public async Task An_unknown_id_is_a_404()
    {
        var response = await PostAsync("suspend", new { reason = Reason, version = 1 }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Suspending_an_already_suspended_author_is_a_409_that_keeps_the_first_reason()
    {
        await SuspendAsync();

        var again = await SuspendAsync("A completely different reason.");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("The account is already suspended.", (await ReadAsync(again)).GetProperty("detail").GetString());
        Assert.Equal(Reason, await ColumnAsync("suspension_reason"));
        Assert.Single(_api.Audit.Events);
    }

    [Fact]
    public async Task A_stale_version_is_a_409_and_changes_nothing()
    {
        var stale = await VersionAsync();
        await _api.SendJsonAsync(HttpMethod.Put, $"/api/admin/users/{_author.Id}/profile", _adminToken,
            new { fullName = "Ann Changed", bio = "x", version = stale });

        var response = await PostAsync("suspend", new { reason = Reason, version = stale });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Active", await ColumnAsync("status"));
        Assert.Equal(1, await ActiveSessionsAsync());
        Assert.DoesNotContain(_api.Audit.Events, e => e.Action == "UserSuspended");
    }

    [Fact]
    public async Task Two_simultaneous_suspensions_let_exactly_one_win_and_audit_once()
    {
        var version = await VersionAsync();

        var responses = await Task.WhenAll(
            PostAsync("suspend", new { reason = "First reason given here.", version }),
            PostAsync("suspend", new { reason = "Second reason given here.", version }));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal("Suspended", await ColumnAsync("status"));
        Assert.Equal(0, await ActiveSessionsAsync());
    }

    [Fact]
    public async Task A_failure_while_suspending_leaves_the_author_active_with_sessions_intact()
    {
        _api.Audit.FailOn = "UserSuspended";

        var response = await SuspendAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        _api.Audit.FailOn = null;
        await AssertUntouchedAsync();
    }

    [Fact]
    public async Task The_articles_submissions_and_reviews_of_the_author_are_not_changed()
    {
        foreach (var status in new[] { "Draft", "PendingReview", "Published", "Rejected" })
        {
            await AddArticleAsync(_author.Id, status);
        }

        var before = await ArticleSnapshotAsync();

        await SuspendAsync();
        var suspended = await ArticleSnapshotAsync();
        await ReactivateAsync();
        var reactivated = await ArticleSnapshotAsync();

        Assert.NotEqual(string.Empty, before);
        Assert.Equal(before, suspended);
        Assert.Equal(before, reactivated);
    }

    // ---- reactivate ----

    [Fact]
    public async Task Reactivating_makes_the_author_active_and_clears_reason_and_time()
    {
        await SuspendAsync();

        var response = await ReactivateAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("suspensionReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("suspendedAt").ValueKind);
        Assert.Equal("Active", await ColumnAsync("status"));
        Assert.Equal("<null>", await ColumnAsync("suspension_reason"));
        Assert.Equal("<null>", await ColumnAsync("suspended_at"));
    }

    [Fact]
    public async Task After_reactivation_the_old_token_stays_invalid_and_a_new_sign_in_works()
    {
        await SuspendAsync();
        await ReactivateAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(_authorToken));
        var login = await _api.LoginAsync("ann@demo.local", ApiHost.Password, "Author");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await ReadAsync(login)).GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, await MeAsync(token));
    }

    [Fact]
    public async Task Reactivating_an_active_author_is_a_409()
    {
        var response = await ReactivateAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("The account is not suspended.", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Empty(_api.Audit.Events);
    }

    [Fact]
    public async Task Reactivating_with_a_stale_version_is_a_409_and_the_author_stays_suspended()
    {
        await SuspendAsync();

        var response = await PostAsync("reactivate", new { version = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Suspended", await ColumnAsync("status"));
    }

    [Fact]
    public async Task Reactivate_validates_the_target_and_the_body()
    {
        var admin = await PostAsync("reactivate", new { version = 1 }, _admin.Id);
        var unknown = await PostAsync("reactivate", new { version = 1 }, Guid.NewGuid());
        var noVersion = await PostAsync("reactivate", new { });

        Assert.Equal(HttpStatusCode.BadRequest, admin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
    }

    [Fact]
    public async Task An_author_can_be_suspended_again_after_reactivation()
    {
        await SuspendAsync();
        await ReactivateAsync();
        var token = await _api.LoginTokenAsync("ann@demo.local", "Author");

        var response = await SuspendAsync("Second offence, harassment.");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Second offence, harassment.", await ColumnAsync("suspension_reason"));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(token));
    }

    // ---- audit ----

    [Fact]
    public async Task Suspending_and_reactivating_write_audit_events_for_the_actor_and_target()
    {
        await SuspendAsync();
        await ReactivateAsync();

        var events = _api.Audit.Events;
        Assert.Equal(["UserSuspended", "UserReactivated"], events.Select(e => e.Action));
        Assert.All(events, e =>
        {
            Assert.Equal("User", e.EntityType);
            Assert.Equal(_author.Id, e.EntityId);
            Assert.Equal(_admin.Id, e.ActorUserId);
            Assert.Equal(_author.Id, JsonDocument.Parse(e.DetailsJson).RootElement.GetProperty("targetUserId").GetGuid());
        });
        Assert.Equal(Reason, JsonDocument.Parse(events[0].DetailsJson).RootElement.GetProperty("reason").GetString());
    }

    // ---- authorization ----

    [Theory]
    [InlineData("suspend")]
    [InlineData("reactivate")]
    public async Task Authors_get_403_and_visitors_401_and_nothing_changes(string action)
    {
        var body = new { reason = Reason, version = await VersionAsync() };

        var author = await PostAsync(action, body, token: _authorToken);
        var anonymous = await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/users/{_author.Id}/{action}", null, body);

        Assert.Equal(HttpStatusCode.Forbidden, author.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Active", await ColumnAsync("status"));
    }
}
