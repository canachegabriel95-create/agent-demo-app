using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Auditing;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Admin;

[Collection(PostgresCollection.Name)]
public class AuditHistoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Route = "/api/admin/audit-events";

    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private Domain.Entities.User _ann = null!;
    private string _adminToken = null!;
    private string _authorToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres, realAudit: true);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _ann = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _authorToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private async Task<JsonElement> ListAsync(string query = "")
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}{query}", _adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string> Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("details").GetProperty("n").GetString()!).ToList();

    private static List<string> Actions(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("action").GetString()!).ToList();

    /// <summary>Inserts an event with a fixed time; <c>details.n</c> names it so tests can read the order.</summary>
    private Task AddAsync(string name, string action, string occurredAt, Guid? actor)
    {
        // The inserted rows leave out the correlation id on purpose: events written outside a request have none.
        return _api.ExecuteAsync(
            "INSERT INTO audit_events (id, occurred_at, actor_user_id, action, entity_type, entity_id, details) " +
            "VALUES (gen_random_uuid(), @at::timestamptz, @actor, @action, 'User', gen_random_uuid(), jsonb_build_object('n', @name::text))",
            ("at", occurredAt), ("actor", (object?)actor ?? DBNull.Value), ("action", action), ("name", name));
    }

    // ---- shape ----

    [Fact]
    public async Task An_event_has_time_action_actor_entity_details_and_correlation_id()
    {
        var response = await _api.SendJsonAsync(HttpMethod.Post, "/api/admin/invitations", _adminToken, new { email = "newbie@demo.local", validForDays = 7 });
        var invitation = await response.Content.ReadFromJsonAsync<JsonElement>();

        var item = (await ListAsync()).GetProperty("items").EnumerateArray().Single();

        Assert.Equal("InvitationCreated", item.GetProperty("action").GetString());
        Assert.Equal(_admin.Id, item.GetProperty("actor").GetProperty("id").GetGuid());
        Assert.Equal("Alice Admin", item.GetProperty("actor").GetProperty("fullName").GetString());
        Assert.Equal("Administrator", item.GetProperty("actor").GetProperty("role").GetString());
        Assert.Equal("Invitation", item.GetProperty("entityType").GetString());
        Assert.Equal(invitation.GetProperty("id").GetGuid(), item.GetProperty("entityId").GetGuid());
        Assert.Equal("newbie@demo.local", item.GetProperty("details").GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.String, item.GetProperty("occurredAt").ValueKind);
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), item.GetProperty("correlationId").GetString());
        Assert.Equal(
            ["action", "actor", "correlationId", "details", "entityId", "entityType", "id", "occurredAt"],
            item.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task An_event_without_an_actor_is_a_system_event()
    {
        await AddAsync("by-system", "AdministratorProvisioned", "2026-10-01T10:00:00Z", null);

        var item = (await ListAsync()).GetProperty("items").EnumerateArray().Single();

        Assert.Equal(JsonValueKind.Null, item.GetProperty("actor").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("correlationId").ValueKind);
    }

    [Fact]
    public async Task An_empty_history_is_an_empty_page()
    {
        var page = await ListAsync();

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    // ---- order, paging ----

    [Fact]
    public async Task The_newest_event_comes_first_and_paging_returns_each_event_once()
    {
        for (var i = 1; i <= 5; i++)
        {
            await AddAsync($"e{i}", "UserSuspended", $"2026-10-0{i}T10:00:00Z", _admin.Id);
        }

        var first = await ListAsync("?page=1&pageSize=2");
        var second = await ListAsync("?page=2&pageSize=2");
        var third = await ListAsync("?page=3&pageSize=2");

        Assert.Equal(["e5", "e4"], Names(first));
        Assert.Equal(["e3", "e2"], Names(second));
        Assert.Equal(["e1"], Names(third));
        Assert.Equal(5, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("pageSize").GetInt32());
        Assert.Empty(Names(await ListAsync("?page=4&pageSize=2")));
    }

    [Fact]
    public async Task Events_with_the_same_time_keep_a_stable_order_across_pages()
    {
        for (var i = 1; i <= 4; i++)
        {
            await AddAsync($"t{i}", "UserSuspended", "2026-10-01T10:00:00Z", _admin.Id);
        }

        var seen = Names(await ListAsync("?page=1&pageSize=2")).Concat(Names(await ListAsync("?page=2&pageSize=2"))).ToList();

        Assert.Equal(4, seen.Distinct().Count());
    }

    // ---- filters ----

    [Fact]
    public async Task The_action_filter_takes_one_or_several_actions()
    {
        await AddAsync("s1", "UserSuspended", "2026-10-01T10:00:00Z", _admin.Id);
        await AddAsync("r1", "UserReactivated", "2026-10-02T10:00:00Z", _admin.Id);
        await AddAsync("a1", "ArticleApproved", "2026-10-03T10:00:00Z", _admin.Id);

        Assert.Equal(["s1"], Names(await ListAsync("?action=UserSuspended")));
        Assert.Equal(["r1", "s1"], Names(await ListAsync("?action=UserSuspended&action=UserReactivated")));
        Assert.Equal(["a1", "r1", "s1"], Names(await ListAsync()));
    }

    [Fact]
    public async Task The_actor_filter_returns_only_that_users_events()
    {
        await AddAsync("by-admin", "UserSuspended", "2026-10-01T10:00:00Z", _admin.Id);
        await AddAsync("by-ann", "ArticleSubmitted", "2026-10-02T10:00:00Z", _ann.Id);
        await AddAsync("by-system", "AdministratorProvisioned", "2026-10-03T10:00:00Z", null);

        Assert.Equal(["by-ann"], Names(await ListAsync($"?actorId={_ann.Id}")));
        Assert.Equal(["by-admin"], Names(await ListAsync($"?actorId={_admin.Id}")));
        Assert.Empty(Names(await ListAsync($"?actorId={Guid.NewGuid()}")));
    }

    [Fact]
    public async Task The_date_range_includes_from_and_excludes_to()
    {
        await AddAsync("before", "UserSuspended", "2026-10-01T23:59:59Z", _admin.Id);
        await AddAsync("at-from", "UserSuspended", "2026-10-02T00:00:00Z", _admin.Id);
        await AddAsync("inside", "UserSuspended", "2026-10-02T12:00:00Z", _admin.Id);
        await AddAsync("at-to", "UserSuspended", "2026-10-03T00:00:00Z", _admin.Id);
        await AddAsync("after", "UserSuspended", "2026-10-03T00:00:01Z", _admin.Id);

        Assert.Equal(
            ["inside", "at-from"],
            Names(await ListAsync("?from=2026-10-02T00:00:00Z&to=2026-10-03T00:00:00Z")));
        Assert.Equal(["after", "at-to"], Names(await ListAsync("?from=2026-10-03T00:00:00Z")));
        Assert.Equal(["before"], Names(await ListAsync("?to=2026-10-02T00:00:00Z")));
        Assert.Empty(Names(await ListAsync("?from=2026-10-02T00:00:00Z&to=2026-10-02T00:00:00Z")));
    }

    [Fact]
    public async Task Filters_combine()
    {
        await AddAsync("match", "UserSuspended", "2026-10-02T12:00:00Z", _admin.Id);
        await AddAsync("wrong-actor", "UserSuspended", "2026-10-02T12:00:00Z", _ann.Id);
        await AddAsync("wrong-action", "UserReactivated", "2026-10-02T12:00:00Z", _admin.Id);
        await AddAsync("wrong-day", "UserSuspended", "2026-10-09T12:00:00Z", _admin.Id);

        var page = await ListAsync($"?action=UserSuspended&actorId={_admin.Id}&from=2026-10-02T00:00:00Z&to=2026-10-03T00:00:00Z");

        Assert.Equal(["match"], Names(page));
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?action=NoSuchAction")]
    [InlineData("?action=UserSuspended&action=Forged")]
    [InlineData("?from=2026-10-03T00:00:00Z&to=2026-10-02T00:00:00Z")]
    [InlineData("?from=yesterday")]
    [InlineData("?to=not-a-date")]
    [InlineData("?actorId=not-a-guid")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task A_bad_query_is_400(string query)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}{query}", _adminToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_reversed_range_names_the_field()
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}?from=2026-10-03T00:00:00Z&to=2026-10-02T00:00:00Z", _adminToken);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("to", out _));
    }

    // ---- actions ----

    [Fact]
    public async Task The_actions_endpoint_lists_every_action_name()
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}/actions", _adminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var names = (await response.Content.ReadFromJsonAsync<string[]>())!;
        Assert.Equal(AuditActions.All, names);
        Assert.Contains("AdministratorProvisioned", names);
        Assert.Contains("ArticleRejected", names);
        Assert.Equal(11, names.Length);
    }

    // ---- access ----

    [Theory]
    [InlineData("/api/admin/audit-events")]
    [InlineData("/api/admin/audit-events/actions")]
    public async Task Authors_get_403_and_visitors_401(string url)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.SendAsync(HttpMethod.Get, url, _authorToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, url, null)).StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Audit_events_cannot_be_written_through_the_api(string method)
    {
        await AddAsync("untouched", "UserSuspended", "2026-10-01T10:00:00Z", _admin.Id);

        foreach (var url in new[] { Route, $"{Route}/actions", $"{Route}/{Guid.NewGuid()}" })
        {
            var response = await _api.SendJsonAsync(new HttpMethod(method), url, _adminToken, new { action = "Forged" });
            Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, $"{method} {url}: {response.StatusCode}");
        }

        Assert.Equal(["untouched"], Names(await ListAsync()));
    }
}
