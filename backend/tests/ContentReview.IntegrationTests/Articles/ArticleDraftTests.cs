using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Articles;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ContentReview.IntegrationTests.Articles;

[Collection(PostgresCollection.Name)]
public class ArticleDraftTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string PendingMessage = "Article cannot be modified in status PendingReview.";

    private ApiHost _api = null!;
    private Domain.Entities.User _ann = null!;
    private Domain.Entities.User _bob = null!;
    private Domain.Entities.User _admin = null!;
    private string _annToken = null!;
    private string _bobToken = null!;
    private string _adminToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
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

    private Task<HttpResponseMessage> PostAsync(object body, string? token = null) =>
        _api.SendJsonAsync(HttpMethod.Post, "/api/articles", token ?? _annToken, body);

    private Task<HttpResponseMessage> GetAsync(Guid id, string? token = null) =>
        _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}", token ?? _annToken);

    private Task<HttpResponseMessage> PutAsync(Guid id, object body, string? token = null) =>
        _api.SendJsonAsync(HttpMethod.Put, $"/api/articles/{id}", token ?? _annToken, body);

    private Task<HttpResponseMessage> DeleteAsync(Guid id, uint? version, string? token = null) =>
        _api.SendAsync(HttpMethod.Delete, $"/api/articles/{id}{(version is null ? "" : $"?version={version}")}", token ?? _annToken);

    private async Task<JsonElement> CreateDraftAsync(string title = "My draft", string? token = null)
    {
        var response = await PostAsync(new { title }, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static Guid IdOf(JsonElement article) => article.GetProperty("id").GetGuid();

    private static uint VersionOf(JsonElement article) => article.GetProperty("version").GetUInt32();

    private async Task<uint> CurrentVersionAsync(Guid id) =>
        VersionOf(await ReadAsync(await GetAsync(id)));

    private Task<string> ColumnAsync(Guid id, string column) =>
        _api.ScalarAsync<string>($"SELECT coalesce({column}::text, '<null>') FROM articles WHERE id = @id", ("id", id));

    private Task<long> CountAsync(string table, Guid articleId) =>
        _api.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE article_id = @id", ("id", articleId));

    private async Task<Guid> InsertArticleAsync(Guid authorId, string status, string title = "Seeded article", int submissionVersion = 0)
    {
        var id = Guid.CreateVersion7();
        await _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, title, summary, body, status, submission_version, created_at, updated_at) " +
            "VALUES (@id, @author, @title, 'Summary', 'Body', @status, @sv, now(), now())",
            ("id", id), ("author", authorId), ("title", title), ("status", status), ("sv", submissionVersion));
        return id;
    }

    private async Task AddHistoryAsync(Guid articleId, int version, string decision, string? reason, DateTimeOffset decidedAt)
    {
        await _api.ExecuteAsync(
            "INSERT INTO article_submissions (id, article_id, version, title, summary, body, category_id, submitted_at) " +
            "VALUES (@id, @a, @v, 't', 's', 'b', @c, now())",
            ("id", Guid.CreateVersion7()), ("a", articleId), ("v", version), ("c", CategorySeed.Technology.Id));
        await _api.ExecuteAsync(
            "INSERT INTO reviews (id, article_id, submission_version, reviewer_id, decision, reason, decided_at) " +
            "VALUES (@id, @a, @v, @r, @d, @reason, @at)",
            ("id", Guid.CreateVersion7()), ("a", articleId), ("v", version), ("r", _admin.Id), ("d", decision),
            ("reason", (object?)reason ?? DBNull.Value), ("at", decidedAt.UtcDateTime));
    }

    // ---- create ----

    [Fact]
    public async Task A_title_is_enough_to_create_a_draft()
    {
        var response = await PostAsync(new { title = "My first draft" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var article = await ReadAsync(response);
        Assert.EndsWith($"/api/articles/{IdOf(article)}", response.Headers.Location!.ToString());
        Assert.Equal("My first draft", article.GetProperty("title").GetString());
        Assert.Equal(string.Empty, article.GetProperty("summary").GetString());
        Assert.Equal(string.Empty, article.GetProperty("body").GetString());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("category").ValueKind);
        Assert.Equal("Draft", article.GetProperty("status").GetString());
        Assert.Equal(0, article.GetProperty("submissionVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("submittedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, article.GetProperty("publishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, article.GetProperty("latestReview").ValueKind);
        Assert.Equal(["edit", "delete", "submit"], article.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(_api.Clock.GetUtcNow(), article.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(article.GetProperty("createdAt").GetDateTimeOffset(), article.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.True(VersionOf(article) > 0);
        Assert.Equal(_ann.Id.ToString(), await ColumnAsync(IdOf(article), "author_id"));
        Assert.Equal("Draft", await ColumnAsync(IdOf(article), "status"));
    }

    [Fact]
    public async Task A_draft_can_carry_everything_and_the_category_comes_back_with_its_name()
    {
        var response = await PostAsync(new
        {
            title = "  Padded title  ",
            summary = "  A summary  ",
            body = "Line one\nLine two",
            categoryId = CategorySeed.Science.Id,
        });

        var article = await ReadAsync(response);
        Assert.Equal("Padded title", article.GetProperty("title").GetString());
        Assert.Equal("A summary", article.GetProperty("summary").GetString());
        Assert.Equal("Line one\nLine two", article.GetProperty("body").GetString());
        Assert.Equal(CategorySeed.Science.Id, article.GetProperty("category").GetProperty("id").GetGuid());
        Assert.Equal("Science", article.GetProperty("category").GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_server_decides_the_status_the_owner_and_the_ids_whatever_the_body_says()
    {
        var response = await PostAsync(new
        {
            title = "Sneaky",
            status = "Published",
            authorId = _bob.Id,
            id = Guid.NewGuid(),
            createdAt = "2001-01-01T00:00:00Z",
            submissionVersion = 7,
            publishedAt = "2001-01-01T00:00:00Z",
        });

        var article = await ReadAsync(response);
        Assert.Equal("Draft", article.GetProperty("status").GetString());
        Assert.Equal(_ann.Id.ToString(), await ColumnAsync(IdOf(article), "author_id"));
        Assert.NotEqual(new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero), article.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(0, article.GetProperty("submissionVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("publishedAt").ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task A_missing_title_is_a_400_and_nothing_is_stored(string? title)
    {
        var response = await PostAsync(new { title, summary = "x", body = "y" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["Title is required."],
            (await ReadAsync(response)).GetProperty("errors").GetProperty("title").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, await _api.ScalarAsync<long>("SELECT count(*) FROM articles"));
    }

    [Fact]
    public async Task A_request_without_a_body_at_all_is_a_400_not_a_500()
    {
        var response = await PostAsync(new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(150, 500, 20000, HttpStatusCode.Created)]
    [InlineData(1, 0, 0, HttpStatusCode.Created)]
    [InlineData(151, 0, 0, HttpStatusCode.BadRequest)]
    [InlineData(1, 501, 0, HttpStatusCode.BadRequest)]
    [InlineData(1, 0, 20001, HttpStatusCode.BadRequest)]
    public async Task The_draft_limits_are_inclusive(int title, int summary, int body, HttpStatusCode expected)
    {
        var response = await PostAsync(new { title = new string('t', title), summary = new string('s', summary), body = new string('b', body) });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Every_broken_limit_is_reported_at_once_with_its_own_message()
    {
        var response = await PostAsync(new
        {
            title = new string('t', 151),
            summary = new string('s', 501),
            body = new string('b', 20001),
            categoryId = Guid.NewGuid(),
        });

        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.Equal("Title must be at most 150 characters.", errors.GetProperty("title")[0].GetString());
        Assert.Equal("Summary must be at most 500 characters.", errors.GetProperty("summary")[0].GetString());
        Assert.Equal("Body must be at most 20000 characters.", errors.GetProperty("body")[0].GetString());
        Assert.Equal("Category does not exist.", errors.GetProperty("categoryId")[0].GetString());
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0197a000-0000-7000-8000-0000000000ff")]
    public async Task A_category_that_does_not_exist_is_a_400(string categoryId)
    {
        var response = await PostAsync(new { title = "T", categoryId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Category does not exist.",
            (await ReadAsync(response)).GetProperty("errors").GetProperty("categoryId")[0].GetString());
        Assert.Equal(0, await _api.ScalarAsync<long>("SELECT count(*) FROM articles"));
    }

    [Fact]
    public async Task NUL_characters_are_removed_instead_of_failing_with_a_500()
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", _annToken, new { title = "Ti\u0000tle", summary = "su\u0000m", body = "bo\u0000dy" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var article = await ReadAsync(response);
        Assert.Equal("Title", article.GetProperty("title").GetString());
        Assert.Equal("sum", article.GetProperty("summary").GetString());
        Assert.Equal("body", article.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Markup_is_stored_and_returned_as_plain_text()
    {
        const string body = "<script>alert(1)</script>\n<img src=x onerror=alert(2)> & more";

        var article = await ReadAsync(await PostAsync(new { title = "<b>Bold?</b>", body }));

        Assert.Equal("<b>Bold?</b>", article.GetProperty("title").GetString());
        Assert.Equal(body, article.GetProperty("body").GetString());
        Assert.Equal(body, await ColumnAsync(IdOf(article), "body"));
    }

    [Fact]
    public async Task Two_authors_may_use_the_same_title()
    {
        await CreateDraftAsync("Same title", _annToken);
        var second = await PostAsync(new { title = "Same title" }, _bobToken);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task A_draft_is_still_there_after_signing_in_again()
    {
        var created = await CreateDraftAsync("Survives");
        await _api.SendAsync(HttpMethod.Post, "/api/auth/logout", _annToken);

        var token = await _api.LoginTokenAsync("ann@demo.local", "Author");
        var again = await ReadAsync(await GetAsync(IdOf(created), token));

        Assert.Equal("Survives", again.GetProperty("title").GetString());
    }

    // ---- read ----

    [Fact]
    public async Task The_owner_reads_the_article_and_nobody_else_can()
    {
        var created = await CreateDraftAsync("Private");
        var id = IdOf(created);

        var owner = await GetAsync(id);
        var other = await GetAsync(id, _bobToken);
        var unknown = await GetAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.Equal(created.GetRawText(), (await ReadAsync(owner)).GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        // Somebody else's article looks exactly like one that does not exist.
        Assert.Equal(await ShapeAsync(other), await ShapeAsync(unknown));
    }

    private static async Task<string> ShapeAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        return string.Join("|", body.EnumerateObject()
            .Where(p => p.Name is not ("correlationId" or "traceId"))
            .Select(p => $"{p.Name}={p.Value}"));
    }

    [Theory]
    [InlineData("Draft", new[] { "edit", "delete", "submit" })]
    [InlineData("PendingReview", new[] { "withdraw" })]
    [InlineData("Rejected", new[] { "edit", "submit" })]
    [InlineData("Published", new string[0])]
    public async Task Allowed_actions_follow_the_lifecycle_state_machine(string status, string[] expected)
    {
        var id = await InsertArticleAsync(_ann.Id, status);

        var article = await ReadAsync(await GetAsync(id));

        Assert.Equal(expected, article.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(status, article.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_latest_review_decision_is_shown_with_its_reason()
    {
        var id = await InsertArticleAsync(_ann.Id, "Rejected", submissionVersion: 2);
        var earlier = _api.Clock.GetUtcNow().AddDays(-2);
        await AddHistoryAsync(id, 1, "Rejected", "First reason given here.", earlier);
        await AddHistoryAsync(id, 2, "Rejected", "Second reason given here.", earlier.AddDays(1));

        var review = (await ReadAsync(await GetAsync(id))).GetProperty("latestReview");

        Assert.Equal("Rejected", review.GetProperty("decision").GetString());
        Assert.Equal("Second reason given here.", review.GetProperty("reason").GetString());
        Assert.Equal(earlier.AddDays(1), review.GetProperty("decidedAt").GetDateTimeOffset());
    }

    // ---- update ----

    [Fact]
    public async Task Updating_saves_the_content_and_returns_the_new_version()
    {
        var created = await CreateDraftAsync("Before");
        _api.Clock.Advance(TimeSpan.FromHours(1));

        var response = await PutAsync(IdOf(created), new
        {
            title = "  After  ",
            summary = "New summary",
            body = "New body",
            categoryId = CategorySeed.Health.Id,
            version = VersionOf(created),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var article = await ReadAsync(response);
        Assert.Equal("After", article.GetProperty("title").GetString());
        Assert.Equal("New summary", article.GetProperty("summary").GetString());
        Assert.Equal("New body", article.GetProperty("body").GetString());
        Assert.Equal("Health", article.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("Draft", article.GetProperty("status").GetString());
        Assert.NotEqual(VersionOf(created), VersionOf(article));
        Assert.Equal(_api.Clock.GetUtcNow(), article.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(created.GetProperty("createdAt").GetDateTimeOffset(), article.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal("After", await ColumnAsync(IdOf(created), "title"));
    }

    [Fact]
    public async Task The_new_version_can_be_used_for_the_next_save_and_a_null_category_clears_it()
    {
        var created = await CreateDraftAsync();
        var first = await ReadAsync(await PutAsync(IdOf(created), new { title = "One", categoryId = CategorySeed.Culture.Id, version = VersionOf(created) }));

        var second = await PutAsync(IdOf(created), new { title = "Two", categoryId = (Guid?)null, version = VersionOf(first) });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(second)).GetProperty("category").ValueKind);
    }

    [Fact]
    public async Task A_rejected_article_can_be_edited_and_stays_rejected()
    {
        var id = await InsertArticleAsync(_ann.Id, "Rejected", submissionVersion: 1);
        var version = await CurrentVersionAsync(id);

        var response = await PutAsync(id, new { title = "Fixed after rejection", version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rejected", (await ReadAsync(response)).GetProperty("status").GetString());
        Assert.Equal(1, (await ReadAsync(await GetAsync(id))).GetProperty("submissionVersion").GetInt32());
    }

    [Theory]
    [InlineData("PendingReview")]
    [InlineData("Published")]
    public async Task An_article_in_review_or_published_cannot_be_edited_and_nothing_changes(string status)
    {
        var id = await InsertArticleAsync(_ann.Id, status, "Locked");
        var version = await CurrentVersionAsync(id);

        var response = await PutAsync(id, new { title = "Changed", version });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"Article cannot be modified in status {status}.", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Equal("Locked", await ColumnAsync(id, "title"));
        Assert.Equal(version, await CurrentVersionAsync(id));
    }

    [Fact]
    public async Task The_status_conflict_is_reported_before_a_stale_version_or_bad_fields()
    {
        var id = await InsertArticleAsync(_ann.Id, "PendingReview");

        var response = await PutAsync(id, new { title = "", version = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(PendingMessage, (await ReadAsync(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Bad_fields_are_a_400_and_the_stored_article_is_unchanged()
    {
        var created = await CreateDraftAsync("Keep me");

        var response = await PutAsync(IdOf(created), new
        {
            title = "",
            summary = new string('s', 501),
            categoryId = Guid.NewGuid(),
            version = VersionOf(created),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("summary", out _));
        Assert.True(errors.TryGetProperty("categoryId", out _));
        Assert.Equal("Keep me", await ColumnAsync(IdOf(created), "title"));
        Assert.Equal(VersionOf(created), await CurrentVersionAsync(IdOf(created)));
    }

    [Fact]
    public async Task A_missing_version_is_a_400()
    {
        var created = await CreateDraftAsync();

        var response = await PutAsync(IdOf(created), new { title = "No version" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("version", out _));
    }

    [Fact]
    public async Task A_stale_version_is_a_409_that_names_the_current_version_and_changes_nothing()
    {
        var created = await CreateDraftAsync("Original");
        var stale = VersionOf(created);
        var current = VersionOf(await ReadAsync(await PutAsync(IdOf(created), new { title = "Newer", version = stale })));

        var response = await PutAsync(IdOf(created), new { title = "Lost update", version = stale });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadAsync(response);
        Assert.Equal(current, problem.GetProperty("currentVersion").GetUInt32());
        Assert.Contains("changed by someone else", problem.GetProperty("detail").GetString());
        Assert.Equal("Newer", await ColumnAsync(IdOf(created), "title"));
    }

    [Fact]
    public async Task Two_saves_with_the_same_version_let_exactly_one_win_and_the_loser_is_told_the_current_version()
    {
        var created = await CreateDraftAsync();
        var version = VersionOf(created);

        var responses = await Task.WhenAll(
            PutAsync(IdOf(created), new { title = "Alpha", version }),
            PutAsync(IdOf(created), new { title = "Beta", version }));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(await CurrentVersionAsync(IdOf(created)), (await ReadAsync(loser)).GetProperty("currentVersion").GetUInt32());
        Assert.Contains(await ColumnAsync(IdOf(created), "title"), new[] { "Alpha", "Beta" });
    }

    [Fact]
    public async Task Nobody_can_edit_somebody_elses_article()
    {
        var created = await CreateDraftAsync("Ann's");

        var response = await PutAsync(IdOf(created), new { title = "Bob was here", version = VersionOf(created) }, _bobToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Ann's", await ColumnAsync(IdOf(created), "title"));
    }

    // ---- delete ----

    [Fact]
    public async Task The_owner_deletes_a_draft()
    {
        var created = await CreateDraftAsync();

        var response = await DeleteAsync(IdOf(created), VersionOf(created));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await _api.ScalarAsync<long>("SELECT count(*) FROM articles WHERE id = @id", ("id", IdOf(created))));
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(IdOf(created))).StatusCode);
    }

    [Theory]
    [InlineData("PendingReview")]
    [InlineData("Published")]
    [InlineData("Rejected")]
    public async Task Only_a_draft_can_be_deleted(string status)
    {
        var id = await InsertArticleAsync(_ann.Id, status);

        var response = await DeleteAsync(id, await CurrentVersionAsync(id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"Article cannot be modified in status {status}.", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM articles WHERE id = @id", ("id", id)));
    }

    [Fact]
    public async Task A_stale_version_on_delete_is_a_409_with_the_current_version_and_the_draft_stays()
    {
        var created = await CreateDraftAsync();
        var stale = VersionOf(created);
        var current = VersionOf(await ReadAsync(await PutAsync(IdOf(created), new { title = "Edited", version = stale })));

        var response = await DeleteAsync(IdOf(created), stale);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(current, (await ReadAsync(response)).GetProperty("currentVersion").GetUInt32());
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM articles WHERE id = @id", ("id", IdOf(created))));
    }

    [Fact]
    public async Task Delete_needs_a_version()
    {
        var created = await CreateDraftAsync();

        var response = await DeleteAsync(IdOf(created), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM articles WHERE id = @id", ("id", IdOf(created))));
    }

    [Fact]
    public async Task Nobody_can_delete_somebody_elses_draft_and_an_unknown_id_is_a_404()
    {
        var created = await CreateDraftAsync();

        var other = await DeleteAsync(IdOf(created), VersionOf(created), _bobToken);
        var unknown = await DeleteAsync(Guid.NewGuid(), 1);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM articles WHERE id = @id", ("id", IdOf(created))));
    }

    [Fact]
    public async Task A_withdrawn_draft_with_a_rejection_in_its_past_can_still_be_deleted_with_its_history()
    {
        var id = await InsertArticleAsync(_ann.Id, "Draft", submissionVersion: 2);
        await AddHistoryAsync(id, 1, "Rejected", "The reason given for rejecting it.", _api.Clock.GetUtcNow().AddDays(-1));
        var other = await InsertArticleAsync(_ann.Id, "Draft", "Untouched");
        await AddHistoryAsync(other, 1, "Rejected", "Another reason for another article.", _api.Clock.GetUtcNow().AddDays(-1));

        var response = await DeleteAsync(id, await CurrentVersionAsync(id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountAsync("reviews", id));
        Assert.Equal(0, await CountAsync("article_submissions", id));
        Assert.Equal(1, await CountAsync("reviews", other));
        Assert.Equal(1, await CountAsync("article_submissions", other));
    }

    [Fact]
    public async Task A_delete_and_a_save_with_the_same_version_let_exactly_one_win()
    {
        var created = await CreateDraftAsync();
        var version = VersionOf(created);

        var responses = await Task.WhenAll(
            DeleteAsync(IdOf(created), version),
            PutAsync(IdOf(created), new { title = "Saved", version }));

        Assert.Equal(1, responses.Count(r => r.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound));
    }

    // ---- who may call ----

    [Fact]
    public async Task Administrators_get_403_and_visitors_401_on_every_endpoint()
    {
        var created = await CreateDraftAsync();
        var id = IdOf(created);

        var admin = new[]
        {
            await PostAsync(new { title = "x" }, _adminToken),
            await GetAsync(id, _adminToken),
            await PutAsync(id, new { title = "x", version = 1 }, _adminToken),
            await DeleteAsync(id, 1, _adminToken),
        };
        var anonymous = new[]
        {
            await _api.SendJsonAsync(HttpMethod.Post, "/api/articles", null, new { title = "x" }),
            await _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}", null),
            await _api.SendJsonAsync(HttpMethod.Put, $"/api/articles/{id}", null, new { title = "x", version = 1 }),
            await _api.SendAsync(HttpMethod.Delete, $"/api/articles/{id}?version=1", null),
        };

        Assert.All(admin, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.All(anonymous, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM articles"));
    }

    [Fact]
    public async Task A_suspended_authors_token_stops_working_and_the_service_refuses_them_too()
    {
        var created = await CreateDraftAsync("Mine");
        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended', suspension_reason = 'Spam links', suspended_at = now() WHERE id = @id", ("id", _ann.Id));

        var viaApi = await GetAsync(IdOf(created));
        using var scope = _api.Services.CreateScope();
        var articles = scope.ServiceProvider.GetRequiredService<IArticleService>();

        Assert.Equal(HttpStatusCode.Unauthorized, viaApi.StatusCode);
        await Assert.ThrowsAsync<ForbiddenException>(() => articles.CreateAsync(_ann.Id, "Blocked", null, null, null));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => articles.UpdateAsync(_ann.Id, IdOf(created), "Blocked", null, null, null, VersionOf(created)));
        await Assert.ThrowsAsync<ForbiddenException>(() => articles.DeleteAsync(_ann.Id, IdOf(created), VersionOf(created)));
        Assert.Equal("Mine", await ColumnAsync(IdOf(created), "title"));
    }

    [Fact]
    public async Task The_service_refuses_an_administrators_id()
    {
        using var scope = _api.Services.CreateScope();
        var articles = scope.ServiceProvider.GetRequiredService<IArticleService>();

        await Assert.ThrowsAsync<NotFoundException>(() => articles.CreateAsync(_admin.Id, "Nope", null, null, null));
    }
}
