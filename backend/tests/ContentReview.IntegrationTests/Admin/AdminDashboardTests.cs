using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Admin;

[Collection(PostgresCollection.Name)]
public class AdminDashboardTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Route = "/api/admin/dashboard";

    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private Domain.Entities.User _ann = null!;
    private string _adminToken = null!;
    private string _authorToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _ann = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _authorToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private async Task<JsonElement> DashboardAsync()
    {
        var response = await _api.SendAsync(HttpMethod.Get, Route, _adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static int[] Numbers(JsonElement d) =>
    [
        d.GetProperty("activeAuthors").GetInt32(),
        d.GetProperty("suspendedAuthors").GetInt32(),
        d.GetProperty("availableInvitations").GetInt32(),
        d.GetProperty("pendingReviews").GetInt32(),
        d.GetProperty("publishedArticles").GetInt32(),
    ];

    private Task AddArticleAsync(string status) =>
        _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, title, summary, body, status, submission_version, published_at, created_at, updated_at) " +
            "VALUES (gen_random_uuid(), @author, 'Title', 'Summary', 'Body', @status, 1, CASE WHEN @status = 'Published' THEN now() END, now(), now())",
            ("author", _ann.Id), ("status", status));

    private Task AddInvitationAsync(string email, string? state = null)
    {
        var used = state == "used" ? "now()" : "NULL";
        var revoked = state == "revoked" ? "now()" : "NULL";
        var expires = state == "expired" ? "now() - interval '1 day'" : "now() + interval '7 days'";
        var created = state == "expired" ? "now() - interval '8 days'" : "now()";
        return _api.ExecuteAsync(
            "INSERT INTO invitations (id, code, normalized_email, email, created_by_user_id, created_at, expires_at, used_at, used_by_user_id, revoked_at, revoked_by_user_id) " +
            $"VALUES (gen_random_uuid(), @code, @email, @email, @admin, {created}, {expires}, {used}, {(state == "used" ? "@ann" : "NULL")}, {revoked}, {(state == "revoked" ? "@admin" : "NULL")})",
            ("code", Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()), ("email", email), ("admin", _admin.Id), ("ann", _ann.Id));
    }

    [Fact]
    public async Task With_only_the_seeded_accounts_the_numbers_are_the_authors_and_zeros()
    {
        // The administrator is not counted; Ann is the only author.
        Assert.Equal(new[] { 1, 0, 0, 0, 0 }, Numbers(await DashboardAsync()));
    }

    [Fact]
    public async Task The_numbers_match_a_known_dataset()
    {
        await _api.SeedUserAsync("bea@demo.local", UserRole.Author);
        await _api.SeedUserAsync("cy@demo.local", UserRole.Author, suspended: true);
        await _api.SeedUserAsync("dee@demo.local", UserRole.Author, suspended: true);
        await _api.SeedUserAsync("root2@demo.local", UserRole.Administrator);
        await AddInvitationAsync("a@x.com");
        await AddInvitationAsync("b@x.com");
        await AddInvitationAsync("c@x.com", "used");
        await AddInvitationAsync("d@x.com", "revoked");
        await AddInvitationAsync("e@x.com", "expired");
        foreach (var status in new[] { "Draft", "PendingReview", "PendingReview", "Published", "Published", "Published", "Rejected" })
        {
            await AddArticleAsync(status);
        }

        var dashboard = await DashboardAsync();

        // Active: Ann and Bea. Suspended: Cy and Dee. Administrators do not count.
        Assert.Equal(new[] { 2, 2, 2, 2, 3 }, Numbers(dashboard));
        Assert.Equal(
            ["activeAuthors", "availableInvitations", "pendingReviews", "publishedArticles", "suspendedAuthors"],
            dashboard.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task An_invitation_stops_being_available_when_it_expires()
    {
        var created = await _api.SendJsonAsync(HttpMethod.Post, "/api/admin/invitations", _adminToken, new { email = "new@x.com", validForDays = 1 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, Numbers(await DashboardAsync())[2]);

        _api.Clock.Advance(TimeSpan.FromHours(23));
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        Assert.Equal(1, Numbers(await DashboardAsync())[2]);

        _api.Clock.Advance(TimeSpan.FromHours(2));
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        Assert.Equal(0, Numbers(await DashboardAsync())[2]);
    }

    [Fact]
    public async Task The_numbers_follow_what_happens_on_the_platform()
    {
        var article = await _api.SendJsonAsync(HttpMethod.Post, "/api/articles", _authorToken,
            new { title = "Moves along", summary = new string('s', 30), body = new string('b', 150), categoryId = ContentReview.Infrastructure.Persistence.CategorySeed.Science.Id });
        var created = await article.Content.ReadFromJsonAsync<JsonElement>();
        var submit = await _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{created.GetProperty("id").GetGuid()}/submit", _authorToken,
            new { version = created.GetProperty("version").GetUInt32() });
        var submitted = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { 1, 0, 0, 1, 0 }, Numbers(await DashboardAsync()));

        var approve = await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/articles/{created.GetProperty("id").GetGuid()}/approve", _adminToken,
            new { version = submitted.GetProperty("version").GetUInt32(), submissionVersion = submitted.GetProperty("submissionVersion").GetInt32() });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(new[] { 1, 0, 0, 0, 1 }, Numbers(await DashboardAsync()));

        var details = await (await _api.SendAsync(HttpMethod.Get, $"/api/admin/users/{_ann.Id}", _adminToken)).Content.ReadFromJsonAsync<JsonElement>();
        var suspend = await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/users/{_ann.Id}/suspend", _adminToken,
            new { reason = "Posting spam links", version = details.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        Assert.Equal(new[] { 0, 1, 0, 0, 1 }, Numbers(await DashboardAsync()));
    }

    [Fact]
    public async Task Authors_get_403_visitors_401_and_nothing_can_be_written()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.SendAsync(HttpMethod.Get, Route, _authorToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, Route, null)).StatusCode);

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            var response = await _api.SendJsonAsync(method, Route, _adminToken, new { activeAuthors = 99 });
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }
}
