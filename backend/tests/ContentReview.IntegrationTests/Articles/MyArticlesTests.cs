using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Articles;

[Collection(PostgresCollection.Name)]
public class MyArticlesTests(PostgresFixture postgres) : IAsyncLifetime
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

    private async Task<JsonElement> ListAsync(string query = "", string? token = null)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"/api/me/articles{query}", token ?? _annToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static List<string> TitlesOf(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToList();

    private async Task<JsonElement> StatsAsync(string? token = null)
    {
        var response = await _api.SendAsync(HttpMethod.Get, "/api/me/articles/stats", token ?? _annToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement> CreateAsync(string title, string? token = null, Guid? categoryId = null, bool complete = true)
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", token ?? _annToken,
            complete
                ? new { title, summary = Summary, body = Body, categoryId = categoryId ?? CategorySeed.Science.Id }
                : (object)new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement> SubmitAsync(JsonElement article, string? token = null)
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{article.GetProperty("id").GetGuid()}/submit", token ?? _annToken,
            new { version = article.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement> DecideAsync(JsonElement submitted, bool approve, string reason = "Please add concrete examples.")
    {
        var id = submitted.GetProperty("id").GetGuid();
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/articles/{id}/{(approve ? "approve" : "reject")}", _adminToken,
            new { version = submitted.GetProperty("version").GetUInt32(), submissionVersion = submitted.GetProperty("submissionVersion").GetInt32(), reason });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static int[] Counts(JsonElement stats) =>
    [
        stats.GetProperty("draft").GetInt32(),
        stats.GetProperty("pendingReview").GetInt32(),
        stats.GetProperty("published").GetInt32(),
        stats.GetProperty("rejected").GetInt32(),
    ];

    private Task SetAsync(Guid id, string column, string value) =>
        _api.ExecuteAsync($"UPDATE articles SET {column} = @v::timestamptz WHERE id = @id", ("v", value), ("id", id));

    // ---- ownership ----

    [Fact]
    public async Task Only_the_callers_own_articles_are_listed()
    {
        await CreateAsync("Ann one");
        await CreateAsync("Ann two");
        await CreateAsync("Bob one", _bobToken);

        Assert.Equal(2, (await ListAsync()).GetProperty("totalCount").GetInt32());
        Assert.Equal(["Bob one"], TitlesOf(await ListAsync(token: _bobToken)));
        Assert.DoesNotContain("Bob one", TitlesOf(await ListAsync()));
    }

    [Fact]
    public async Task Another_authors_published_article_is_not_in_my_list_but_my_own_is()
    {
        var bobs = await SubmitAsync(await CreateAsync("Bob published", _bobToken), _bobToken);
        await DecideAsync(bobs, approve: true);
        var mine = await SubmitAsync(await CreateAsync("Ann published"));
        await DecideAsync(mine, approve: true);

        Assert.Equal(["Ann published"], TitlesOf(await ListAsync()));
    }

    [Fact]
    public async Task A_query_cannot_ask_for_somebody_elses_articles()
    {
        await CreateAsync("Bob one", _bobToken);

        var page = await ListAsync($"?authorId={_bob.Id}&userId={_bob.Id}");

        Assert.Empty(TitlesOf(page));
    }

    [Fact]
    public async Task Administrators_get_403_and_visitors_401()
    {
        foreach (var url in new[] { "/api/me/articles", "/api/me/articles/stats" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await _api.SendAsync(HttpMethod.Get, url, _adminToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, url, null)).StatusCode);
        }
    }

    // ---- the item ----

    [Fact]
    public async Task An_item_has_title_category_status_dates_actions_and_version()
    {
        var created = await CreateAsync("Shaped", categoryId: CategorySeed.Health.Id);

        var item = (await ListAsync()).GetProperty("items")[0];

        Assert.Equal(created.GetProperty("id").GetGuid(), item.GetProperty("id").GetGuid());
        Assert.Equal("Shaped", item.GetProperty("title").GetString());
        Assert.Equal("Health", item.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("Draft", item.GetProperty("status").GetString());
        Assert.True(item.TryGetProperty("updatedAt", out _));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("submittedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("publishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("latestRejectionReason").ValueKind);
        Assert.Equal(["edit", "delete", "submit"], item.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(created.GetProperty("version").GetUInt32(), item.GetProperty("version").GetUInt32());
        Assert.False(item.TryGetProperty("body", out _));
    }

    [Fact]
    public async Task A_draft_without_a_category_has_a_null_category()
    {
        await CreateAsync("Bare", complete: false);

        var item = (await ListAsync()).GetProperty("items")[0];

        Assert.Equal(JsonValueKind.Null, item.GetProperty("category").ValueKind);
    }

    [Theory]
    [InlineData("Draft", new[] { "edit", "delete", "submit" })]
    [InlineData("PendingReview", new[] { "withdraw" })]
    [InlineData("Rejected", new[] { "edit", "submit" })]
    [InlineData("Published", new[] { "revise" })]
    public async Task Allowed_actions_follow_the_lifecycle_for_each_status(string status, string[] expected)
    {
        var article = await CreateAsync("By status");
        var id = article.GetProperty("id").GetGuid();
        await _api.ExecuteAsync("UPDATE articles SET status = @s, submission_version = 1 WHERE id = @id", ("s", status), ("id", id));

        var item = (await ListAsync($"?status={status}")).GetProperty("items")[0];

        Assert.Equal(expected, item.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public async Task A_rejected_article_carries_the_latest_rejection_reason_and_other_statuses_do_not()
    {
        var first = await SubmitAsync(await CreateAsync("Twice rejected"));
        await DecideAsync(first, approve: false, reason: "First rejection reason here.");
        var again = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{first.GetProperty("id").GetGuid()}/submit", _annToken,
            new { version = (await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"/api/articles/{first.GetProperty("id").GetGuid()}", _annToken))).GetProperty("version").GetUInt32() });
        var second = await ReadAsync(again);
        await DecideAsync(second, approve: false, reason: "Second rejection reason here.");
        await DecideDraftSiblingAsync();

        var items = (await ListAsync()).GetProperty("items").EnumerateArray().ToList();

        var rejected = items.Single(i => i.GetProperty("title").GetString() == "Twice rejected");
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("Second rejection reason here.", rejected.GetProperty("latestRejectionReason").GetString());
        Assert.All(items.Where(i => i.GetProperty("status").GetString() != "Rejected"),
            i => Assert.Equal(JsonValueKind.Null, i.GetProperty("latestRejectionReason").ValueKind));
    }

    private async Task DecideDraftSiblingAsync() => await CreateAsync("Plain draft");

    [Fact]
    public async Task A_published_article_has_its_publication_date_and_a_submission_date()
    {
        var submitted = await SubmitAsync(await CreateAsync("Out there"));
        var decided = await DecideAsync(submitted, approve: true);

        var item = (await ListAsync()).GetProperty("items")[0];

        Assert.Equal("Published", item.GetProperty("status").GetString());
        Assert.Equal(decided.GetProperty("publishedAt").GetDateTimeOffset(), item.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.String, item.GetProperty("submittedAt").ValueKind);
    }

    // ---- filters ----

    [Fact]
    public async Task Search_finds_a_part_of_the_title_ignoring_case_and_treats_wildcards_literally()
    {
        await CreateAsync("Learning Rust Basics");
        await CreateAsync("Cooking 100% vegan");
        await CreateAsync("Plain title");
        await CreateAsync("Snake_case rules");

        Assert.Equal(["Learning Rust Basics"], TitlesOf(await ListAsync("?search=RUST")));
        Assert.Equal(["Cooking 100% vegan"], TitlesOf(await ListAsync("?search=100%25")));
        Assert.Equal(["Snake_case rules"], TitlesOf(await ListAsync("?search=ke_c")));
        // A bare % matches only the title that really contains one, not every title.
        Assert.Equal(["Cooking 100% vegan"], TitlesOf(await ListAsync("?search=%25")));
        Assert.Equal(4, (await ListAsync("?search=%20%20")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Filtering_by_status_and_by_category()
    {
        await CreateAsync("Draft science");
        await CreateAsync("Draft health", categoryId: CategorySeed.Health.Id);
        await SubmitAsync(await CreateAsync("Pending science"));

        Assert.Equal(["Pending science"], TitlesOf(await ListAsync("?status=PendingReview")));
        Assert.Equal(["Draft health"], TitlesOf(await ListAsync($"?categoryId={CategorySeed.Health.Id}")));
        Assert.Equal(["Draft science"], TitlesOf(await ListAsync($"?status=Draft&categoryId={CategorySeed.Science.Id}")));
        Assert.Empty(TitlesOf(await ListAsync("?status=Published")));
    }

    [Theory]
    [InlineData("?status=Nonsense")]
    [InlineData("?sort=author")]
    [InlineData("?direction=sideways")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?categoryId=not-a-guid")]
    public async Task Bad_query_values_are_a_400(string query)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"/api/me/articles{query}", _annToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- sorting ----

    [Fact]
    public async Task The_default_is_the_most_recently_updated_first()
    {
        var a = await CreateAsync("Old");
        var b = await CreateAsync("Middle");
        var c = await CreateAsync("New");
        await SetAsync(a.GetProperty("id").GetGuid(), "updated_at", "2026-01-01T10:00:00Z");
        await SetAsync(b.GetProperty("id").GetGuid(), "updated_at", "2026-02-01T10:00:00Z");
        await SetAsync(c.GetProperty("id").GetGuid(), "updated_at", "2026-03-01T10:00:00Z");

        Assert.Equal(["New", "Middle", "Old"], TitlesOf(await ListAsync()));
        Assert.Equal(["Old", "Middle", "New"], TitlesOf(await ListAsync("?sort=updatedAt&direction=asc")));
    }

    [Fact]
    public async Task Sorting_by_title_defaults_to_a_to_z_ignoring_case()
    {
        await CreateAsync("banana");
        await CreateAsync("Apple");
        await CreateAsync("cherry");

        Assert.Equal(["Apple", "banana", "cherry"], TitlesOf(await ListAsync("?sort=title")));
        Assert.Equal(["cherry", "banana", "Apple"], TitlesOf(await ListAsync("?sort=title&direction=desc")));
    }

    [Fact]
    public async Task Sorting_by_submission_date_puts_never_submitted_drafts_last_in_both_directions()
    {
        var early = await SubmitAsync(await CreateAsync("Early bird"));
        var late = await SubmitAsync(await CreateAsync("Late bloom"));
        await CreateAsync("Never submitted");
        await SetAsync(early.GetProperty("id").GetGuid(), "submitted_at", "2026-01-01T10:00:00Z");
        await SetAsync(late.GetProperty("id").GetGuid(), "submitted_at", "2026-02-01T10:00:00Z");

        Assert.Equal(["Late bloom", "Early bird", "Never submitted"], TitlesOf(await ListAsync("?sort=submittedAt")));
        Assert.Equal(["Early bird", "Late bloom", "Never submitted"], TitlesOf(await ListAsync("?sort=submittedAt&direction=asc")));
    }

    // ---- paging ----

    [Fact]
    public async Task The_list_is_paged_without_repeating_or_skipping_articles()
    {
        for (var i = 1; i <= 5; i++)
        {
            var created = await CreateAsync($"Article {i}");
            await SetAsync(created.GetProperty("id").GetGuid(), "updated_at", $"2026-01-0{i}T10:00:00Z");
        }

        var first = await ListAsync("?pageSize=2&page=1");
        var second = await ListAsync("?pageSize=2&page=2");
        var third = await ListAsync("?pageSize=2&page=3");
        var beyond = await ListAsync("?pageSize=2&page=4");

        Assert.Equal(["Article 5", "Article 4"], TitlesOf(first));
        Assert.Equal(["Article 3", "Article 2"], TitlesOf(second));
        Assert.Equal(["Article 1"], TitlesOf(third));
        Assert.Empty(TitlesOf(beyond));
        Assert.All(new[] { first, second, third, beyond }, p => Assert.Equal(5, p.GetProperty("totalCount").GetInt32()));
        Assert.Equal(20, (await ListAsync()).GetProperty("pageSize").GetInt32());
    }

    // ---- stats ----

    [Fact]
    public async Task Stats_have_all_four_statuses_and_zeros_for_a_new_author()
    {
        var stats = await StatsAsync();

        Assert.Equal(new[] { 0, 0, 0, 0 }, Counts(stats));
        Assert.Equal(["draft", "pendingReview", "published", "rejected"], stats.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Stats_follow_submit_withdraw_approve_and_reject_on_the_next_request()
    {
        var one = await CreateAsync("Article one");
        var two = await CreateAsync("Article two");
        var three = await CreateAsync("Article three");
        await CreateAsync("Article four");
        Assert.Equal(new[] { 4, 0, 0, 0 }, Counts(await StatsAsync()));

        var oneSubmitted = await SubmitAsync(one);
        var twoSubmitted = await SubmitAsync(two);
        var threeSubmitted = await SubmitAsync(three);
        Assert.Equal(new[] { 1, 3, 0, 0 }, Counts(await StatsAsync()));

        await DecideAsync(oneSubmitted, approve: true);
        Assert.Equal(new[] { 1, 2, 1, 0 }, Counts(await StatsAsync()));

        await DecideAsync(twoSubmitted, approve: false);
        Assert.Equal(new[] { 1, 1, 1, 1 }, Counts(await StatsAsync()));

        var withdrawn = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{threeSubmitted.GetProperty("id").GetGuid()}/withdraw", _annToken,
            new { version = threeSubmitted.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal(new[] { 2, 0, 1, 1 }, Counts(await StatsAsync()));

        var deleted = await _api.SendAsync(
            HttpMethod.Delete, $"/api/articles/{threeSubmitted.GetProperty("id").GetGuid()}?version={(await ReadAsync(withdrawn)).GetProperty("version").GetUInt32()}", _annToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(new[] { 1, 0, 1, 1 }, Counts(await StatsAsync()));
    }

    [Fact]
    public async Task Stats_count_only_the_callers_articles()
    {
        await CreateAsync("Ann draft");
        await CreateAsync("Bob draft 1", _bobToken);
        await CreateAsync("Bob draft 2", _bobToken);

        Assert.Equal(new[] { 1, 0, 0, 0 }, Counts(await StatsAsync()));
        Assert.Equal(new[] { 2, 0, 0, 0 }, Counts(await StatsAsync(_bobToken)));
    }

    [Fact]
    public async Task A_suspended_authors_token_stops_working()
    {
        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended', suspension_reason = 'Spam links', suspended_at = now() WHERE id = @id", ("id", _ann.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, "/api/me/articles", _annToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, "/api/me/articles/stats", _annToken)).StatusCode);
    }
}
