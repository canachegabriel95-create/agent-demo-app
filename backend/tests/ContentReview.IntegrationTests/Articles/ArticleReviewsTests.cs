using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Articles;

[Collection(PostgresCollection.Name)]
public class ArticleReviewsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private Domain.Entities.User _ann = null!;
    private Domain.Entities.User _admin = null!;
    private string _annToken = null!;
    private string _bobToken = null!;
    private string _adminToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _ann = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        await _api.SeedUserAsync("bob@demo.local", UserRole.Author, fullName: "Bob Writer");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _annToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
        _bobToken = await _api.LoginTokenAsync("bob@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private Task<HttpResponseMessage> ListAsync(Guid id, string? token) =>
        _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}/reviews", token);

    private async Task<Guid> InsertArticleAsync(string status, int submissionVersion)
    {
        var id = Guid.CreateVersion7();
        await _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, category_id, title, summary, body, status, submission_version, created_at, updated_at) " +
            "VALUES (@id, @author, @c, 'Reviewed article', 'Summary', 'Body', @status, @sv, now(), now())",
            ("id", id), ("author", _ann.Id), ("c", CategorySeed.Science.Id), ("status", status), ("sv", submissionVersion));
        return id;
    }

    private async Task AddReviewAsync(Guid articleId, int version, string decision, string? reason, int hoursAgo)
    {
        await _api.ExecuteAsync(
            "INSERT INTO article_submissions (id, article_id, version, title, summary, body, category_id, submitted_at) " +
            "VALUES (@id, @a, @v, 't', 's', 'b', @c, now())",
            ("id", Guid.CreateVersion7()), ("a", articleId), ("v", version), ("c", CategorySeed.Science.Id));
        await _api.ExecuteAsync(
            "INSERT INTO reviews (id, article_id, submission_version, reviewer_id, decision, reason, decided_at) " +
            "VALUES (@id, @a, @v, @r, @d, @reason, now() - make_interval(hours => @h))",
            ("id", Guid.CreateVersion7()), ("a", articleId), ("v", version), ("r", _admin.Id), ("d", decision),
            ("reason", (object?)reason ?? DBNull.Value), ("h", hoursAgo));
    }

    [Fact]
    public async Task The_owner_gets_every_decision_newest_submission_first()
    {
        var id = await InsertArticleAsync("Published", 2);
        await AddReviewAsync(id, 1, "Rejected", "Needs more sources and examples.", hoursAgo: 48);
        await AddReviewAsync(id, 2, "Approved", null, hoursAgo: 2);

        var response = await ListAsync(id, _annToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reviews = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal(2, reviews.Count);
        Assert.Equal(2, reviews[0].GetProperty("submissionVersion").GetInt32());
        Assert.Equal("Approved", reviews[0].GetProperty("decision").GetString());
        Assert.Equal(JsonValueKind.Null, reviews[0].GetProperty("reason").ValueKind);
        Assert.Equal(1, reviews[1].GetProperty("submissionVersion").GetInt32());
        Assert.Equal("Rejected", reviews[1].GetProperty("decision").GetString());
        Assert.Equal("Needs more sources and examples.", reviews[1].GetProperty("reason").GetString());
        Assert.True(reviews[1].TryGetProperty("decidedAt", out _));
    }

    [Fact]
    public async Task The_reviewer_is_never_exposed_to_the_author()
    {
        var id = await InsertArticleAsync("Rejected", 1);
        await AddReviewAsync(id, 1, "Rejected", "Please rework the introduction.", hoursAgo: 1);

        var response = await ListAsync(id, _annToken);
        var text = await response.Content.ReadAsStringAsync();

        var review = JsonDocument.Parse(text).RootElement[0];
        Assert.Equal(
            ["submissionVersion", "decision", "reason", "decidedAt"],
            review.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(_admin.Id.ToString(), text);
        Assert.DoesNotContain("Alice", text);
        Assert.DoesNotContain("reviewer", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_article_without_decisions_has_an_empty_history()
    {
        var id = await InsertArticleAsync("Draft", 0);

        var response = await ListAsync(id, _annToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }

    [Fact]
    public async Task Another_author_gets_the_same_404_as_for_an_article_that_does_not_exist()
    {
        var id = await InsertArticleAsync("Rejected", 1);
        await AddReviewAsync(id, 1, "Rejected", "Private feedback for Ann only.", hoursAgo: 1);

        var other = await ListAsync(id, _bobToken);
        var unknown = await ListAsync(Guid.NewGuid(), _bobToken);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.DoesNotContain("Private feedback", await other.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Administrators_get_403_and_visitors_401()
    {
        var id = await InsertArticleAsync("Draft", 0);

        Assert.Equal(HttpStatusCode.Forbidden, (await ListAsync(id, _adminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ListAsync(id, null)).StatusCode);
    }
}
