using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Articles;

[Collection(PostgresCollection.Name)]
public class FeedTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string Summary = new('s', 30);
    private static readonly string Body = new('b', 150);

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
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _annToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
        _bobToken = await _api.LoginTokenAsync("bob@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    // ---- helpers ----

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> CreateAsync(string title, string token, Guid? categoryId = null, string? body = null)
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", token,
            new { title, summary = Summary, body = body ?? Body, categoryId = categoryId ?? CategorySeed.Science.Id });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement> SubmitAsync(JsonElement article, string token)
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{article.GetProperty("id").GetGuid()}/submit", token,
            new { version = article.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task DecideAsync(JsonElement submitted, bool approve)
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/articles/{submitted.GetProperty("id").GetGuid()}/{(approve ? "approve" : "reject")}", _adminToken,
            new
            {
                version = submitted.GetProperty("version").GetUInt32(),
                submissionVersion = submitted.GetProperty("submissionVersion").GetInt32(),
                reason = "Please add concrete examples.",
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Writes, submits and approves an article; returns its id.</summary>
    private async Task<Guid> PublishAsync(string title, string token, Guid? categoryId = null, string? body = null)
    {
        var submitted = await SubmitAsync(await CreateAsync(title, token, categoryId, body), token);
        await DecideAsync(submitted, approve: true);
        return submitted.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> FeedAsync(string query = "", string? token = null)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"/api/feed{query}", token ?? _annToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static List<string> TitlesOf(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToList();

    private Task SetAsync(Guid id, string column, string value) =>
        _api.ExecuteAsync($"UPDATE articles SET {column} = @v::timestamptz WHERE id = @id", ("v", value), ("id", id));

    // ---- only Published ----

    [Fact]
    public async Task The_feed_lists_only_published_articles_of_all_authors()
    {
        await PublishAsync("Ann published", _annToken);
        await PublishAsync("Bob published", _bobToken);
        await CreateAsync("Ann draft", _annToken);
        await SubmitAsync(await CreateAsync("Ann pending", _annToken), _annToken);
        var rejected = await SubmitAsync(await CreateAsync("Bob rejected", _bobToken), _bobToken);
        await DecideAsync(rejected, approve: false);

        var titles = TitlesOf(await FeedAsync());

        Assert.Equal(["Ann published", "Bob published"], titles.Order());
        Assert.Equal(2, (await FeedAsync()).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_published_article_stays_in_the_feed_after_its_author_is_suspended()
    {
        var id = await PublishAsync("Bob stays", _bobToken);
        await _api.ExecuteAsync(
            "UPDATE users SET status = 'Suspended', suspension_reason = 'Spam links', suspended_at = now() WHERE id = @id", ("id", _bob.Id));

        Assert.Equal(["Bob stays"], TitlesOf(await FeedAsync()));
        var detail = await _api.SendAsync(HttpMethod.Get, $"/api/feed/{id}", _annToken);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("Bob Writer", (await ReadAsync(detail)).GetProperty("authorDisplayName").GetString());
    }

    [Fact]
    public async Task An_empty_feed_is_an_empty_page()
    {
        var page = await FeedAsync();

        Assert.Empty(TitlesOf(page));
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    // ---- the item ----

    [Fact]
    public async Task An_item_has_title_summary_category_author_name_and_date_and_nothing_that_identifies_the_author()
    {
        var id = await PublishAsync("Shaped", _bobToken, CategorySeed.Health.Id);

        var item = (await FeedAsync()).GetProperty("items")[0];

        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("Shaped", item.GetProperty("title").GetString());
        Assert.Equal(Summary, item.GetProperty("summary").GetString());
        Assert.Equal("Health", item.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("Bob Writer", item.GetProperty("authorDisplayName").GetString());
        Assert.Equal(JsonValueKind.String, item.GetProperty("publishedAt").ValueKind);
        Assert.NotEqual(default, item.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.False(item.TryGetProperty("body", out _));
        var raw = item.GetRawText();
        Assert.DoesNotContain("bob@demo.local", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_bob.Id.ToString(), raw);
        Assert.Equal(["authorDisplayName", "category", "id", "publishedAt", "summary", "title"],
            item.EnumerateObject().Select(p => p.Name).Order());
    }

    // ---- detail ----

    [Fact]
    public async Task The_detail_is_the_approved_text_with_author_name_and_without_author_identity()
    {
        var id = await PublishAsync("Detailed", _bobToken, body: new string('x', 200));

        var response = await _api.SendAsync(HttpMethod.Get, $"/api/feed/{id}", _annToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadAsync(response);
        Assert.Equal("Detailed", detail.GetProperty("title").GetString());
        Assert.Equal(Summary, detail.GetProperty("summary").GetString());
        Assert.Equal(new string('x', 200), detail.GetProperty("body").GetString());
        Assert.Equal("Science", detail.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("Bob Writer", detail.GetProperty("authorDisplayName").GetString());
        Assert.Equal(["authorDisplayName", "body", "category", "id", "publishedAt", "summary", "title"],
            detail.EnumerateObject().Select(p => p.Name).Order());
        Assert.DoesNotContain("bob@demo.local", detail.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_detail_is_the_current_published_content_held_by_the_article_row()
    {
        // A published article is read-only except through an approved revision, which replaces this very content.
        var id = await PublishAsync("As approved", _bobToken, body: new string('a', 150));
        await _api.ExecuteAsync("UPDATE articles SET title = 'Replaced by a revision', body = 'New body' WHERE id = @id", ("id", id));

        var detail = await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"/api/feed/{id}", _annToken));

        Assert.Equal("Replaced by a revision", detail.GetProperty("title").GetString());
        Assert.Equal("New body", detail.GetProperty("body").GetString());
        Assert.Equal(["Replaced by a revision"], TitlesOf(await FeedAsync()));
    }

    [Fact]
    public async Task A_draft_pending_rejected_or_unknown_id_is_404_on_detail()
    {
        var draft = await CreateAsync("Secret draft", _bobToken);
        var pending = await SubmitAsync(await CreateAsync("Secret pending", _bobToken), _bobToken);
        var rejected = await SubmitAsync(await CreateAsync("Secret rejected", _bobToken), _bobToken);
        await DecideAsync(rejected, approve: false);

        foreach (var id in new[]
                 {
                     draft.GetProperty("id").GetGuid(),
                     pending.GetProperty("id").GetGuid(),
                     rejected.GetProperty("id").GetGuid(),
                     Guid.NewGuid(),
                 })
        {
            var response = await _api.SendAsync(HttpMethod.Get, $"/api/feed/{id}", _annToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Secret", await response.Content.ReadAsStringAsync());
        }

        // Even the owner reads their own unpublished article through the article endpoints, not here.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _api.SendAsync(HttpMethod.Get, $"/api/feed/{draft.GetProperty("id").GetGuid()}", _bobToken)).StatusCode);
    }

    [Fact]
    public async Task A_published_article_cannot_be_taken_back_so_its_entry_is_stable()
    {
        var id = await PublishAsync("Once only", _bobToken);

        var current = await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}", _bobToken));
        var withdraw = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{id}/withdraw", _bobToken, new { version = current.GetProperty("version").GetUInt32() });

        Assert.Equal(HttpStatusCode.Conflict, withdraw.StatusCode);
        Assert.Equal(["Once only"], TitlesOf(await FeedAsync()));
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("PendingReview")]
    [InlineData("Rejected")]
    public async Task The_status_is_checked_by_the_query_itself_even_when_an_approved_review_exists(string status)
    {
        // An article that has an approval on record but is not Published (however it got there) must not show.
        var id = await PublishAsync("Not anymore", _bobToken);
        await _api.ExecuteAsync("UPDATE articles SET status = @s WHERE id = @id", ("s", status), ("id", id));

        Assert.Empty(TitlesOf(await FeedAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await _api.SendAsync(HttpMethod.Get, $"/api/feed/{id}", _annToken)).StatusCode);
    }

    // ---- search, category, sort, paging ----

    [Fact]
    public async Task Search_matches_part_of_the_title_ignoring_case_with_literal_wildcards()
    {
        await PublishAsync("Cooking 100% vegan", _annToken);
        await PublishAsync("Cooking for two", _annToken);
        await PublishAsync("Gardening basics", _bobToken);

        Assert.Equal(["Cooking 100% vegan", "Cooking for two"], TitlesOf(await FeedAsync("?search=COOKING")).Order());
        Assert.Equal(["Cooking 100% vegan"], TitlesOf(await FeedAsync("?search=100%25")));
        Assert.DoesNotContain("Gardening basics", TitlesOf(await FeedAsync("?search=%25")));
        Assert.Equal(["Gardening basics"], TitlesOf(await FeedAsync("?search=%20basics%20")));
        Assert.Empty(TitlesOf(await FeedAsync("?search=nothing")));
    }

    [Fact]
    public async Task Search_does_not_find_unpublished_titles()
    {
        await CreateAsync("Findable draft", _bobToken);
        await PublishAsync("Findable published", _bobToken);

        Assert.Equal(["Findable published"], TitlesOf(await FeedAsync("?search=findable")));
    }

    [Fact]
    public async Task The_category_filter_narrows_the_feed()
    {
        await PublishAsync("Science one", _annToken, CategorySeed.Science.Id);
        await PublishAsync("Health one", _bobToken, CategorySeed.Health.Id);

        Assert.Equal(["Health one"], TitlesOf(await FeedAsync($"?categoryId={CategorySeed.Health.Id}")));
        Assert.Empty(TitlesOf(await FeedAsync($"?categoryId={Guid.NewGuid()}")));
    }

    [Fact]
    public async Task The_newest_publication_comes_first()
    {
        var oldest = await PublishAsync("Oldest", _annToken);
        var newest = await PublishAsync("Newest", _bobToken);
        var middle = await PublishAsync("Middle", _annToken);
        await SetAsync(oldest, "published_at", "2026-01-01T00:00:00Z");
        await SetAsync(middle, "published_at", "2026-02-01T00:00:00Z");
        await SetAsync(newest, "published_at", "2026-03-01T00:00:00Z");

        Assert.Equal(["Newest", "Middle", "Oldest"], TitlesOf(await FeedAsync()));
    }

    [Fact]
    public async Task Paging_returns_every_article_once_with_the_total()
    {
        for (var i = 1; i <= 5; i++)
        {
            var id = await PublishAsync($"Article {i}", i % 2 == 0 ? _annToken : _bobToken);
            await SetAsync(id, "published_at", $"2026-01-0{i}T00:00:00Z");
        }

        var first = await FeedAsync("?page=1&pageSize=2");
        var second = await FeedAsync("?page=2&pageSize=2");
        var third = await FeedAsync("?page=3&pageSize=2");

        Assert.Equal(["Article 5", "Article 4"], TitlesOf(first));
        Assert.Equal(["Article 3", "Article 2"], TitlesOf(second));
        Assert.Equal(["Article 1"], TitlesOf(third));
        Assert.Equal(5, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, third.GetProperty("page").GetInt32());
        Assert.Empty(TitlesOf(await FeedAsync("?page=4&pageSize=2")));
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?categoryId=not-a-guid")]
    public async Task A_bad_query_is_400(string query)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"/api/feed{query}", _annToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- access ----

    [Fact]
    public async Task Visitors_get_401_and_administrators_403()
    {
        var id = await PublishAsync("Guarded", _annToken);
        foreach (var url in new[] { "/api/feed", $"/api/feed/{id}" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, url, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _api.SendAsync(HttpMethod.Get, url, _adminToken)).StatusCode);
        }
    }

    [Fact]
    public async Task A_suspended_reader_is_401()
    {
        await _api.ExecuteAsync(
            "UPDATE users SET status = 'Suspended', suspension_reason = 'Spam links', suspended_at = now() WHERE id = @id", ("id", _ann.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, "/api/feed", _annToken)).StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task There_are_no_write_operations_under_the_feed(string method)
    {
        var id = await PublishAsync("Read only", _bobToken);

        foreach (var url in new[] { "/api/feed", $"/api/feed/{id}" })
        {
            var response = await _api.SendJsonAsync(new HttpMethod(method), url, _bobToken, new { title = "Hijacked" });
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        Assert.Equal(["Read only"], TitlesOf(await FeedAsync()));
    }
}
