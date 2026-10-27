using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Reviews;

[Collection(PostgresCollection.Name)]
public class ReviewQueueTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private Domain.Entities.User _ann = null!;
    private Domain.Entities.User _bob = null!;
    private string _adminToken = null!;
    private string _annToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _ann = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _bob = await _api.SeedUserAsync("bob@demo.local", UserRole.Author, fullName: "Bob Writer");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _annToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    // ---- helpers ----

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private Task<HttpResponseMessage> GetAsync(string url, string? token = null) =>
        _api.SendAsync(HttpMethod.Get, url, token ?? _adminToken);

    private async Task<JsonElement> QueueAsync(string query = "")
    {
        var response = await GetAsync($"/api/admin/reviews/queue{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static List<string> TitlesOf(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToList();

    /// <summary>Inserts an article that was submitted (version <paramref name="version"/>) with its snapshot.</summary>
    private async Task<Guid> InsertSubmittedAsync(
        Guid authorId,
        string title,
        int hoursAgo = 1,
        string status = "PendingReview",
        int version = 1,
        Guid? categoryId = null,
        string summary = "Submitted summary",
        string body = "Submitted body")
    {
        var id = Guid.CreateVersion7();
        var category = categoryId ?? CategorySeed.Science.Id;
        await _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, category_id, title, summary, body, status, submission_version, submitted_at, created_at, updated_at) " +
            "VALUES (@id, @author, @c, @title, @summary, @body, @status, @sv, now() - make_interval(hours => @h), now(), now())",
            ("id", id), ("author", authorId), ("c", category), ("title", title), ("summary", summary), ("body", body),
            ("status", status), ("sv", version), ("h", hoursAgo));
        for (var v = 1; v <= version; v++)
        {
            await _api.ExecuteAsync(
                "INSERT INTO article_submissions (id, article_id, version, title, summary, body, category_id, submitted_at) " +
                "VALUES (@sid, @a, @v, @t, @s, @b, @c, now() - make_interval(hours => @h))",
                ("sid", Guid.CreateVersion7()), ("a", id), ("v", v), ("t", v == version ? title : $"{title} v{v}"),
                ("s", summary), ("b", body), ("c", category), ("h", hoursAgo + (version - v) * 24));
        }

        return id;
    }

    private async Task AddReviewAsync(Guid articleId, int version, string decision, string? reason, int hoursAgo = 1)
    {
        await _api.ExecuteAsync(
            "INSERT INTO reviews (id, article_id, submission_version, reviewer_id, decision, reason, decided_at) " +
            "VALUES (@id, @a, @v, @r, @d, @reason, now() - make_interval(hours => @h))",
            ("id", Guid.CreateVersion7()), ("a", articleId), ("v", version), ("r", _admin.Id), ("d", decision),
            ("reason", (object?)reason ?? DBNull.Value), ("h", hoursAgo));
    }

    private async Task SuspendAsync(Guid userId) =>
        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE id = @id", ("id", userId));

    // ---- queue ----

    [Fact]
    public async Task The_queue_holds_only_pending_articles()
    {
        await InsertSubmittedAsync(_ann.Id, "Pending one");
        await InsertSubmittedAsync(_ann.Id, "Rejected one", status: "Rejected");
        await InsertSubmittedAsync(_ann.Id, "Published one", status: "Published");
        await InsertSubmittedAsync(_ann.Id, "Withdrawn one", status: "Draft");
        await _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, title, summary, body, status, submission_version, created_at, updated_at) " +
            "VALUES (gen_random_uuid(), @a, 'Never submitted', '', '', 'Draft', 0, now(), now())", ("a", _ann.Id));

        var page = await QueueAsync();

        Assert.Equal(["Pending one"], TitlesOf(page));
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task An_item_has_the_article_category_author_with_status_and_submission_details()
    {
        var id = await InsertSubmittedAsync(_ann.Id, "Shaped", hoursAgo: 5, version: 2, categoryId: CategorySeed.Health.Id);

        var item = (await QueueAsync()).GetProperty("items")[0];

        Assert.Equal(id, item.GetProperty("articleId").GetGuid());
        Assert.Equal("Shaped", item.GetProperty("title").GetString());
        Assert.Equal(CategorySeed.Health.Id, item.GetProperty("category").GetProperty("id").GetGuid());
        Assert.Equal("Health", item.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal(_ann.Id, item.GetProperty("author").GetProperty("id").GetGuid());
        Assert.Equal("Ann Author", item.GetProperty("author").GetProperty("fullName").GetString());
        Assert.Equal("Active", item.GetProperty("author").GetProperty("status").GetString());
        Assert.Equal(2, item.GetProperty("submissionVersion").GetInt32());
        Assert.True(item.TryGetProperty("submittedAt", out _));
        Assert.False(item.TryGetProperty("body", out _));
        Assert.False(item.GetProperty("author").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task The_oldest_submission_comes_first_by_default_and_the_direction_can_be_reversed()
    {
        await InsertSubmittedAsync(_ann.Id, "Middle", hoursAgo: 10);
        await InsertSubmittedAsync(_ann.Id, "Newest", hoursAgo: 1);
        await InsertSubmittedAsync(_ann.Id, "Oldest", hoursAgo: 30);

        Assert.Equal(["Oldest", "Middle", "Newest"], TitlesOf(await QueueAsync()));
        Assert.Equal(["Oldest", "Middle", "Newest"], TitlesOf(await QueueAsync("?sort=submittedAt&direction=asc")));
        Assert.Equal(["Newest", "Middle", "Oldest"], TitlesOf(await QueueAsync("?sort=submittedAt&direction=desc")));
    }

    [Fact]
    public async Task Filtering_by_author_by_category_and_by_both()
    {
        await InsertSubmittedAsync(_ann.Id, "Ann science", hoursAgo: 4, categoryId: CategorySeed.Science.Id);
        await InsertSubmittedAsync(_ann.Id, "Ann health", hoursAgo: 3, categoryId: CategorySeed.Health.Id);
        await InsertSubmittedAsync(_bob.Id, "Bob science", hoursAgo: 2, categoryId: CategorySeed.Science.Id);

        Assert.Equal(["Ann science", "Ann health"], TitlesOf(await QueueAsync($"?authorId={_ann.Id}")));
        Assert.Equal(["Ann science", "Bob science"], TitlesOf(await QueueAsync($"?categoryId={CategorySeed.Science.Id}")));
        Assert.Equal(["Ann health"], TitlesOf(await QueueAsync($"?authorId={_ann.Id}&categoryId={CategorySeed.Health.Id}")));
        Assert.Empty(TitlesOf(await QueueAsync($"?authorId={Guid.NewGuid()}")));
    }

    [Fact]
    public async Task The_queue_is_paged_without_repeating_or_skipping_articles()
    {
        for (var i = 1; i <= 5; i++)
        {
            await InsertSubmittedAsync(_ann.Id, $"Article {i}", hoursAgo: 60 - i);
        }

        var first = await QueueAsync("?pageSize=2&page=1");
        var second = await QueueAsync("?pageSize=2&page=2");
        var third = await QueueAsync("?pageSize=2&page=3");
        var beyond = await QueueAsync("?pageSize=2&page=4");

        Assert.Equal(["Article 1", "Article 2"], TitlesOf(first));
        Assert.Equal(["Article 3", "Article 4"], TitlesOf(second));
        Assert.Equal(["Article 5"], TitlesOf(third));
        Assert.Empty(TitlesOf(beyond));
        Assert.All(new[] { first, second, third, beyond }, p => Assert.Equal(5, p.GetProperty("totalCount").GetInt32()));
        Assert.Equal(2, second.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Articles_with_the_same_submission_time_keep_a_stable_order_across_pages()
    {
        await _api.ExecuteAsync("SELECT 1");
        var when = "2026-01-01T10:00:00Z";
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var id = await InsertSubmittedAsync(_ann.Id, $"Tie {i}");
            await _api.ExecuteAsync("UPDATE articles SET submitted_at = @t::timestamptz WHERE id = @id", ("t", when), ("id", id));
            ids.Add(id);
        }

        var one = await QueueAsync("?pageSize=2&page=1");
        var two = await QueueAsync("?pageSize=2&page=2");

        var seen = one.GetProperty("items").EnumerateArray().Concat(two.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("articleId").GetGuid()).ToList();
        Assert.Equal(4, seen.Distinct().Count());
        Assert.Equal(ids.OrderBy(i => i).ToList(), seen);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?direction=sideways")]
    [InlineData("?sort=title")]
    [InlineData("?authorId=not-a-guid")]
    public async Task Bad_query_values_are_a_400(string query)
    {
        var response = await GetAsync($"/api/admin/reviews/queue{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_suspended_authors_pending_article_stays_in_the_queue_marked_suspended()
    {
        await InsertSubmittedAsync(_bob.Id, "Bob waits");
        await SuspendAsync(_bob.Id);

        var item = (await QueueAsync()).GetProperty("items")[0];

        Assert.Equal("Bob waits", item.GetProperty("title").GetString());
        Assert.Equal("Suspended", item.GetProperty("author").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Authors_get_403_and_visitors_401_on_every_review_endpoint()
    {
        var id = await InsertSubmittedAsync(_ann.Id, "Mine");
        var urls = new[] { "/api/admin/reviews/queue", $"/api/admin/articles/{id}", "/api/admin/authors/lookup" };

        foreach (var url in urls)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(url, _annToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, url, null)).StatusCode);
        }
    }

    // ---- detail ----

    [Fact]
    public async Task The_detail_shows_the_submitted_snapshot_not_the_authors_later_edits()
    {
        var id = await InsertSubmittedAsync(_ann.Id, "As submitted", summary: "Summary as submitted", body: "Body as submitted");
        await _api.ExecuteAsync(
            "UPDATE articles SET title = 'Edited later', summary = 'Edited summary', body = 'Edited body', category_id = @c WHERE id = @id",
            ("c", CategorySeed.Culture.Id), ("id", id));

        var response = await GetAsync($"/api/admin/articles/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var article = await ReadAsync(response);
        Assert.Equal("As submitted", article.GetProperty("title").GetString());
        Assert.Equal("Summary as submitted", article.GetProperty("summary").GetString());
        Assert.Equal("Body as submitted", article.GetProperty("body").GetString());
        Assert.Equal("Science", article.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("PendingReview", article.GetProperty("status").GetString());
        Assert.Equal(1, article.GetProperty("submissionVersion").GetInt32());
        Assert.True(article.TryGetProperty("submittedAt", out var submittedAt) && submittedAt.ValueKind == JsonValueKind.String);
        Assert.Equal(JsonValueKind.Null, article.GetProperty("publishedAt").ValueKind);
    }

    [Fact]
    public async Task The_detail_has_the_author_with_email_and_account_status()
    {
        var id = await InsertSubmittedAsync(_ann.Id, "About Ann");

        var author = (await ReadAsync(await GetAsync($"/api/admin/articles/{id}"))).GetProperty("author");

        Assert.Equal(_ann.Id, author.GetProperty("id").GetGuid());
        Assert.Equal("Ann Author", author.GetProperty("fullName").GetString());
        Assert.Equal("ann@demo.local", author.GetProperty("email").GetString());
        Assert.Equal("Active", author.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_detail_has_the_whole_review_history_across_versions_with_reviewers()
    {
        var id = await InsertSubmittedAsync(_ann.Id, "Second time lucky", version: 3, status: "PendingReview");
        await AddReviewAsync(id, 1, "Rejected", "First rejection reason here.", hoursAgo: 72);
        await AddReviewAsync(id, 2, "Rejected", "Second rejection reason here.", hoursAgo: 48);

        var history = (await ReadAsync(await GetAsync($"/api/admin/articles/{id}"))).GetProperty("reviewHistory").EnumerateArray().ToList();

        Assert.Equal(2, history.Count);
        Assert.Equal(2, history[0].GetProperty("submissionVersion").GetInt32());
        Assert.Equal("Second rejection reason here.", history[0].GetProperty("reason").GetString());
        Assert.Equal(1, history[1].GetProperty("submissionVersion").GetInt32());
        Assert.Equal("Rejected", history[1].GetProperty("decision").GetString());
        Assert.Equal(_admin.Id, history[0].GetProperty("reviewer").GetProperty("id").GetGuid());
        Assert.Equal("Alice Admin", history[0].GetProperty("reviewer").GetProperty("fullName").GetString());
        Assert.True(history[0].TryGetProperty("decidedAt", out _));
    }

    [Fact]
    public async Task A_pending_article_of_an_active_author_can_be_reviewed()
    {
        var id = await InsertSubmittedAsync(_ann.Id, "Ready");

        var article = await ReadAsync(await GetAsync($"/api/admin/articles/{id}"));

        Assert.True(article.GetProperty("canReview").GetBoolean());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("reviewBlockedReason").ValueKind);
        Assert.True(article.GetProperty("version").GetUInt32() > 0);
    }

    [Fact]
    public async Task A_suspended_authors_article_cannot_be_reviewed_and_says_why()
    {
        var id = await InsertSubmittedAsync(_bob.Id, "Blocked");
        await SuspendAsync(_bob.Id);

        var article = await ReadAsync(await GetAsync($"/api/admin/articles/{id}"));

        Assert.False(article.GetProperty("canReview").GetBoolean());
        Assert.Equal("Author account is suspended.", article.GetProperty("reviewBlockedReason").GetString());
        Assert.Equal("Suspended", article.GetProperty("author").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("Published")]
    [InlineData("Rejected")]
    [InlineData("Draft")]
    public async Task An_article_that_is_not_pending_can_be_read_but_not_reviewed(string status)
    {
        var id = await InsertSubmittedAsync(_ann.Id, "Decided", status: status);

        var response = await GetAsync($"/api/admin/articles/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var article = await ReadAsync(response);
        Assert.False(article.GetProperty("canReview").GetBoolean());
        Assert.Contains("not pending review", article.GetProperty("reviewBlockedReason").GetString());
        Assert.Equal(status, article.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_draft_that_was_never_submitted_is_private_and_not_found()
    {
        var id = Guid.CreateVersion7();
        await _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, title, summary, body, status, submission_version, created_at, updated_at) " +
            "VALUES (@id, @a, 'Secret draft', 'private', 'private', 'Draft', 0, now(), now())", ("id", id), ("a", _ann.Id));

        var response = await GetAsync($"/api/admin/articles/{id}");
        var unknown = await GetAsync($"/api/admin/articles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.DoesNotContain("private", await response.Content.ReadAsStringAsync());
    }

    // ---- author lookup ----

    [Fact]
    public async Task Author_lookup_finds_by_name_or_email_ignoring_case_and_lists_only_authors()
    {
        var byName = await ReadAsync(await GetAsync("/api/admin/authors/lookup?search=ANN"));
        var byEmail = await ReadAsync(await GetAsync("/api/admin/authors/lookup?search=bob@demo"));
        var admin = await ReadAsync(await GetAsync("/api/admin/authors/lookup?search=alice"));

        Assert.Equal(["Ann Author"], byName.EnumerateArray().Select(a => a.GetProperty("fullName").GetString()));
        Assert.Equal(_ann.Id, byName[0].GetProperty("id").GetGuid());
        Assert.Equal(["Bob Writer"], byEmail.EnumerateArray().Select(a => a.GetProperty("fullName").GetString()));
        Assert.Empty(admin.EnumerateArray());
        Assert.Equal(["id", "fullName"], byName[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Author_lookup_returns_at_most_20_authors_by_name_and_treats_wildcards_literally()
    {
        for (var i = 0; i < 25; i++)
        {
            await _api.SeedUserAsync($"writer{i:00}@demo.local", UserRole.Author, fullName: $"Writer {i:00}");
        }

        var all = await ReadAsync(await GetAsync("/api/admin/authors/lookup"));
        var wildcard = await ReadAsync(await GetAsync("/api/admin/authors/lookup?search=%25"));

        Assert.Equal(20, all.GetArrayLength());
        var names = all.EnumerateArray().Select(a => a.GetProperty("fullName").GetString()!).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
        Assert.Empty(wildcard.EnumerateArray());
    }

    [Fact]
    public async Task Author_lookup_rejects_a_search_that_is_too_long()
    {
        var response = await GetAsync($"/api/admin/authors/lookup?search={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
