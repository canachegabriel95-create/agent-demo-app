using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;

namespace ContentReview.IntegrationTests.Infrastructure;

/// <summary>
/// An API with an administrator and two authors (Ann and Bob), and helpers that walk articles and revisions through
/// the HTTP API the way the portals do.
/// </summary>
public sealed class RevisionScenario : IAsyncDisposable
{
    public static readonly string Summary = new('s', 30);
    public static readonly string Body = new('b', 150);

    private RevisionScenario(ApiHost api)
    {
        Api = api;
    }

    public ApiHost Api { get; }
    public Domain.Entities.User Admin { get; private set; } = null!;
    public Domain.Entities.User Ann { get; private set; } = null!;
    public Domain.Entities.User Bob { get; private set; } = null!;
    public string AdminToken { get; private set; } = null!;
    public string AnnToken { get; private set; } = null!;
    public string BobToken { get; private set; } = null!;

    public static async Task<RevisionScenario> CreateAsync(PostgresFixture postgres, bool realAudit = false)
    {
        var scenario = new RevisionScenario(await ApiHost.CreateAsync(postgres, realAudit: realAudit));
        var api = scenario.Api;
        scenario.Admin = await api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        scenario.Ann = await api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        scenario.Bob = await api.SeedUserAsync("bob@demo.local", UserRole.Author, fullName: "Bob Writer");
        scenario.AdminToken = await api.LoginTokenAsync("admin@demo.local", "Admin");
        scenario.AnnToken = await api.LoginTokenAsync("ann@demo.local", "Author");
        scenario.BobToken = await api.LoginTokenAsync("bob@demo.local", "Author");
        return scenario;
    }

    public ValueTask DisposeAsync() => Api.DisposeAsync();

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static uint VersionOf(JsonElement element) => element.GetProperty("version").GetUInt32();

    /// <summary>Creates a complete article as Ann, has it submitted and an administrator approve it. Returns its id.</summary>
    public async Task<Guid> PublishedArticleAsync(string title = "A published article", string? authorToken = null)
    {
        var token = authorToken ?? AnnToken;
        var created = await Api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", token, new { title, summary = Summary, body = Body, categoryId = CategorySeed.Science.Id });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var article = await ReadAsync(created);
        var id = article.GetProperty("id").GetGuid();

        var submitted = await Api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{id}/submit", token, new { version = VersionOf(article) });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var pending = await ReadAsync(submitted);

        var approved = await Api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/articles/{id}/approve", AdminToken,
            new { version = VersionOf(pending), submissionVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return id;
    }

    public Task<HttpResponseMessage> CreateRevisionAsync(Guid articleId, string? token = null) =>
        Api.SendAsync(HttpMethod.Post, $"/api/articles/{articleId}/revision", token ?? AnnToken);

    public Task<HttpResponseMessage> GetRevisionAsync(Guid articleId, string? token = null) =>
        Api.SendAsync(HttpMethod.Get, $"/api/articles/{articleId}/revision", token ?? AnnToken);

    public Task<HttpResponseMessage> UpdateRevisionAsync(Guid articleId, object body, string? token = null) =>
        Api.SendJsonAsync(HttpMethod.Put, $"/api/articles/{articleId}/revision", token ?? AnnToken, body);

    public Task<HttpResponseMessage> SubmitRevisionAsync(Guid articleId, uint version, string? token = null) =>
        Api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{articleId}/revision/submit", token ?? AnnToken, new { version });

    public Task<HttpResponseMessage> WithdrawRevisionAsync(Guid articleId, uint version, string? token = null) =>
        Api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{articleId}/revision/withdraw", token ?? AnnToken, new { version });

    public Task<HttpResponseMessage> ApproveRevisionAsync(Guid revisionId, uint version, int submissionVersion, string? token = null) =>
        Api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/revisions/{revisionId}/approve", token ?? AdminToken, new { version, submissionVersion });

    public Task<HttpResponseMessage> RejectRevisionAsync(
        Guid revisionId, uint version, int submissionVersion, string? reason = "The new introduction is not accurate.", string? token = null) =>
        Api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/revisions/{revisionId}/reject", token ?? AdminToken, new { version, submissionVersion, reason });

    /// <summary>Creates a revision, gives it new content that meets the submission rules and returns it (Draft).</summary>
    public async Task<JsonElement> DraftRevisionAsync(Guid articleId, string title = "A revised title")
    {
        var created = await CreateRevisionAsync(articleId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var revision = await ReadAsync(created);

        var updated = await UpdateRevisionAsync(articleId, new
        {
            title,
            summary = "A revised summary of the article.",
            body = new string('r', 150),
            categoryId = CategorySeed.Science.Id,
            version = VersionOf(revision),
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        return await ReadAsync(updated);
    }

    /// <summary>A revision with new content that the author has submitted (PendingReview, submission 1).</summary>
    public async Task<JsonElement> PendingRevisionAsync(Guid articleId, string title = "A revised title")
    {
        var draft = await DraftRevisionAsync(articleId, title);
        var submitted = await SubmitRevisionAsync(articleId, VersionOf(draft));
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return await ReadAsync(submitted);
    }

    public async Task<JsonElement> GetArticleAsync(Guid id, string? token = null)
    {
        var response = await Api.SendAsync(HttpMethod.Get, $"/api/articles/{id}", token ?? AnnToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }
}
