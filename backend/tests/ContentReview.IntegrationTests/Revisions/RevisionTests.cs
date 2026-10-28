using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;
using Npgsql;

namespace ContentReview.IntegrationTests.Revisions;

/// <summary>F17.1: an author revises a published article; the published version stays until a revision is approved.</summary>
[Collection(PostgresCollection.Name)]
public class RevisionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string Summary = new('s', 30);
    private static readonly string Body = new('b', 150);
    private static readonly string NewSummary = new('n', 40);
    private static readonly string NewBody = new('r', 200);
    private const string Reason = "Please add concrete examples and sources.";

    private ApiHost _api = null!;
    private ContentReview.Domain.Entities.User _admin = null!;
    private ContentReview.Domain.Entities.User _ann = null!;
    private string _adminToken = null!;
    private string _annToken = null!;
    private string _bobToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _ann = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        await _api.SeedUserAsync("bob@demo.local", UserRole.Author, fullName: "Bob Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _annToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
        _bobToken = await _api.LoginTokenAsync("bob@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    // ---- helpers ----

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await ReadAsync(response);
    }

    /// <summary>Writes, submits and approves an article, exactly as in the portals. Returns its id.</summary>
    private async Task<Guid> PublishAsync(string title = "A published article", string? token = null)
    {
        var created = await OkAsync(await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", token ?? _annToken,
            new { title, summary = Summary, body = Body, categoryId = CategorySeed.Science.Id }));
        var id = created.GetProperty("id").GetGuid();
        var submitted = await OkAsync(await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{id}/submit", token ?? _annToken, new { version = created.GetProperty("version").GetUInt32() }));
        await OkAsync(await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/articles/{id}/approve", _adminToken,
            new { version = submitted.GetProperty("version").GetUInt32(), submissionVersion = 1 }));
        return id;
    }

    private Task<HttpResponseMessage> CreateRevisionRawAsync(Guid articleId, string? token = null) =>
        _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{articleId}/revisions", token ?? _annToken, new { });

    private async Task<JsonElement> CreateRevisionAsync(Guid articleId) => await OkAsync(await CreateRevisionRawAsync(articleId));

    private Task<HttpResponseMessage> EditRawAsync(
        JsonElement revision, string title = "A revised title", string? summary = null, string? body = null, uint? version = null,
        string? token = null) =>
        _api.SendJsonAsync(
            HttpMethod.Put, $"/api/revisions/{revision.GetProperty("id").GetGuid()}", token ?? _annToken,
            new
            {
                title,
                summary = summary ?? NewSummary,
                body = body ?? NewBody,
                categoryId = CategorySeed.Technology.Id,
                version = version ?? revision.GetProperty("version").GetUInt32(),
            });

    private async Task<JsonElement> EditAsync(JsonElement revision, string title = "A revised title") =>
        await OkAsync(await EditRawAsync(revision, title));

    private Task<HttpResponseMessage> RevisionActionRawAsync(JsonElement revision, string action, string? token = null, uint? version = null) =>
        _api.SendJsonAsync(
            HttpMethod.Post, $"/api/revisions/{revision.GetProperty("id").GetGuid()}/{action}", token ?? _annToken,
            new { version = version ?? revision.GetProperty("version").GetUInt32() });

    /// <summary>An edited revision, submitted for review.</summary>
    private async Task<JsonElement> SubmittedRevisionAsync(Guid articleId, string title = "A revised title")
    {
        var edited = await EditAsync(await CreateRevisionAsync(articleId), title);
        return await OkAsync(await RevisionActionRawAsync(edited, "submit"));
    }

    private Task<HttpResponseMessage> ApproveRawAsync(JsonElement revision, uint? version = null, int? submissionVersion = null) =>
        _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/revisions/{revision.GetProperty("id").GetGuid()}/approve", _adminToken,
            new
            {
                version = version ?? revision.GetProperty("version").GetUInt32(),
                submissionVersion = submissionVersion ?? revision.GetProperty("submissionVersion").GetInt32(),
            });

    private Task<HttpResponseMessage> RejectRawAsync(JsonElement revision, string? reason = Reason) =>
        _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/revisions/{revision.GetProperty("id").GetGuid()}/reject", _adminToken,
            new
            {
                version = revision.GetProperty("version").GetUInt32(),
                submissionVersion = revision.GetProperty("submissionVersion").GetInt32(),
                reason,
            });

    private async Task<JsonElement> ArticleAsync(Guid id, string? token = null) =>
        await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}", token ?? _annToken));

    private async Task<JsonElement> FeedItemAsync(Guid id) =>
        await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/feed/{id}", _bobToken));

    private async Task<JsonElement> RevisionAsync(JsonElement revision, string? token = null) =>
        await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/revisions/{revision.GetProperty("id").GetGuid()}", token ?? _annToken));

    private Task<long> CountAsync(string sql, params (string, object)[] parameters) => _api.ScalarAsync<long>(sql, parameters);

    private async Task SuspendAnnAsync() =>
        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE id = @id", ("id", _ann.Id));

    // ---- creating a revision ----

    [Fact]
    public async Task A_published_article_can_be_revised_and_starts_as_a_draft_copy()
    {
        var id = await PublishAsync("Original title");

        var response = await CreateRevisionRawAsync(id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains($"/api/revisions/", response.Headers.Location!.ToString());
        var revision = await ReadAsync(response);
        Assert.Equal(id, revision.GetProperty("articleId").GetGuid());
        Assert.Equal("Draft", revision.GetProperty("status").GetString());
        Assert.Equal("Original title", revision.GetProperty("title").GetString());
        Assert.Equal(Summary, revision.GetProperty("summary").GetString());
        Assert.Equal(Body, revision.GetProperty("body").GetString());
        Assert.Equal(CategorySeed.Science.Id, revision.GetProperty("category").GetProperty("id").GetGuid());
        Assert.Equal(0, revision.GetProperty("submissionVersion").GetInt32());
        Assert.Equal(["edit", "delete", "submit"], revision.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public async Task The_article_shows_its_open_revision_and_stops_offering_revise()
    {
        var id = await PublishAsync();
        Assert.Equal(["revise"], (await ArticleAsync(id)).GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));

        var revision = await CreateRevisionAsync(id);

        var article = await ArticleAsync(id);
        Assert.Equal("Published", article.GetProperty("status").GetString());
        Assert.Empty(article.GetProperty("allowedActions").EnumerateArray());
        var open = article.GetProperty("openRevision");
        Assert.Equal(revision.GetProperty("id").GetGuid(), open.GetProperty("id").GetGuid());
        Assert.Equal("Draft", open.GetProperty("status").GetString());
        Assert.Equal(1, article.GetProperty("publishedVersion").GetInt32());
    }

    [Fact]
    public async Task A_published_article_stays_read_only_for_the_normal_edit_endpoints()
    {
        var id = await PublishAsync();
        var article = await ArticleAsync(id);

        var edit = await _api.SendJsonAsync(
            HttpMethod.Put, $"/api/articles/{id}", _annToken,
            new { title = "Changed directly", summary = Summary, body = Body, version = article.GetProperty("version").GetUInt32() });
        var delete = await _api.SendAsync(
            HttpMethod.Delete, $"/api/articles/{id}?version={article.GetProperty("version").GetUInt32()}", _annToken);

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Creating_a_revision_does_not_touch_the_published_version_or_the_feed()
    {
        var id = await PublishAsync("Stable title");
        var before = await _api.ScalarAsync<string>("SELECT xmin::text FROM articles WHERE id = @id", ("id", id));

        await CreateRevisionAsync(id);

        Assert.Equal(before, await _api.ScalarAsync<string>("SELECT xmin::text FROM articles WHERE id = @id", ("id", id)));
        Assert.Equal("Stable title", (await FeedItemAsync(id)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Only_one_revision_per_article_can_be_open()
    {
        var id = await PublishAsync();
        await CreateRevisionAsync(id);

        var second = await CreateRevisionRawAsync(id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("revision in progress", (await ReadAsync(second)).GetProperty("detail").GetString());
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM article_revisions WHERE article_id = @id", ("id", id)));
    }

    [Fact]
    public async Task Simultaneous_requests_to_start_a_revision_create_exactly_one()
    {
        for (var round = 0; round < 6; round++)
        {
            var id = await PublishAsync($"Start race {round}");

            var responses = await Task.WhenAll(CreateRevisionRawAsync(id), CreateRevisionRawAsync(id), CreateRevisionRawAsync(id));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await CountAsync("SELECT count(*) FROM article_revisions WHERE article_id = @id", ("id", id)));
        }
    }

    [Fact]
    public async Task The_database_itself_refuses_a_second_open_revision()
    {
        var id = await PublishAsync();
        await CreateRevisionAsync(id);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => _api.ExecuteAsync(
            """
            INSERT INTO article_revisions (id, article_id, category_id, title, summary, body, status, submission_version, created_at, updated_at)
            SELECT gen_random_uuid(), article_id, category_id, title, summary, body, 'Draft', 0, now(), now()
            FROM article_revisions WHERE article_id = @id
            """,
            ("id", id)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
    }

    [Fact]
    public async Task An_article_that_is_not_published_cannot_be_revised()
    {
        var draft = await OkAsync(await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", _annToken, new { title = "Only a draft", summary = Summary, body = Body, categoryId = CategorySeed.Science.Id }));
        var draftId = draft.GetProperty("id").GetGuid();

        var onDraft = await CreateRevisionRawAsync(draftId);
        await OkAsync(await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{draftId}/submit", _annToken, new { version = draft.GetProperty("version").GetUInt32() }));
        var onPending = await CreateRevisionRawAsync(draftId);

        Assert.Equal(HttpStatusCode.Conflict, onDraft.StatusCode);
        Assert.Contains("cannot be revised in status Draft", (await ReadAsync(onDraft)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, onPending.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM article_revisions"));
    }

    [Fact]
    public async Task Somebody_elses_article_and_revision_look_like_they_do_not_exist()
    {
        var id = await PublishAsync();
        var revision = await CreateRevisionAsync(id);
        var revisionId = revision.GetProperty("id").GetGuid();
        var version = revision.GetProperty("version").GetUInt32();

        var responses = new[]
        {
            await CreateRevisionRawAsync(id, _bobToken),
            await _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}/revisions", _bobToken),
            await _api.SendAsync(HttpMethod.Get, $"/api/revisions/{revisionId}", _bobToken),
            await EditRawAsync(revision, token: _bobToken),
            await _api.SendAsync(HttpMethod.Delete, $"/api/revisions/{revisionId}?version={version}", _bobToken),
            await RevisionActionRawAsync(revision, "submit", _bobToken),
            await RevisionActionRawAsync(revision, "withdraw", _bobToken),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal("Draft", await _api.ScalarAsync<string>("SELECT status FROM article_revisions WHERE id = @id", ("id", revisionId)));
    }

    [Fact]
    public async Task Revision_endpoints_need_an_author_session()
    {
        var id = await PublishAsync();
        var revision = await CreateRevisionAsync(id);
        var revisionId = revision.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, $"/api/revisions/{revisionId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.SendAsync(HttpMethod.Get, $"/api/revisions/{revisionId}", _adminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateRevisionRawAsync(id, _adminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{id}/revisions", null, new { })).StatusCode);
    }

    [Fact]
    public async Task A_suspended_author_cannot_start_a_revision()
    {
        var id = await PublishAsync();
        await SuspendAnnAsync();

        // The session no longer works: suspension takes effect immediately.
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateRevisionRawAsync(id)).StatusCode);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM article_revisions"));
    }

    // ---- editing ----

    [Fact]
    public async Task Editing_a_revision_changes_it_and_leaves_the_published_article_alone()
    {
        var id = await PublishAsync("Original title");
        var revision = await CreateRevisionAsync(id);

        var edited = await EditAsync(revision, "  Edited title  ");

        Assert.Equal("Edited title", edited.GetProperty("title").GetString());
        Assert.Equal(NewSummary, edited.GetProperty("summary").GetString());
        Assert.Equal(CategorySeed.Technology.Id, edited.GetProperty("category").GetProperty("id").GetGuid());
        Assert.NotEqual(revision.GetProperty("version").GetUInt32(), edited.GetProperty("version").GetUInt32());
        var published = await FeedItemAsync(id);
        Assert.Equal("Original title", published.GetProperty("title").GetString());
        Assert.Equal(Summary, published.GetProperty("summary").GetString());
        Assert.Equal(Body, published.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Edits_follow_the_draft_limits_and_report_every_broken_field()
    {
        var revision = await CreateRevisionAsync(await PublishAsync());

        var response = await EditRawAsync(revision, title: "   ", summary: new string('s', 501), body: new string('b', 20001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("summary", out _));
        Assert.True(errors.TryGetProperty("body", out _));
        Assert.Equal(Body, await _api.ScalarAsync<string>("SELECT body FROM article_revisions"));
    }

    [Fact]
    public async Task Editing_with_a_stale_version_is_a_conflict_that_names_the_current_version()
    {
        var revision = await CreateRevisionAsync(await PublishAsync());
        var edited = await EditAsync(revision);

        var stale = await EditRawAsync(revision, "Old copy", version: revision.GetProperty("version").GetUInt32());

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await ReadAsync(stale);
        Assert.Equal(edited.GetProperty("version").GetUInt32(), problem.GetProperty("currentVersion").GetUInt32());
        Assert.Equal("A revised title", await _api.ScalarAsync<string>("SELECT title FROM article_revisions"));
    }

    [Fact]
    public async Task A_revision_that_is_pending_review_is_read_only()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());

        var edit = await EditRawAsync(submitted, "Sneaky edit");
        var delete = await _api.SendAsync(
            HttpMethod.Delete, $"/api/revisions/{submitted.GetProperty("id").GetGuid()}?version={submitted.GetProperty("version").GetUInt32()}", _annToken);

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Contains("cannot be modified in status PendingReview", (await ReadAsync(edit)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    // ---- submit, withdraw, discard ----

    [Fact]
    public async Task Submitting_applies_the_submission_rules_and_changes_nothing_when_they_fail()
    {
        var revision = await CreateRevisionAsync(await PublishAsync());
        var short_ = await OkAsync(await EditRawAsync(revision, "abc", "too short", "too short"));

        var response = await RevisionActionRawAsync(short_, "submit");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("summary", out _));
        Assert.True(errors.TryGetProperty("body", out _));
        Assert.Equal("Draft", await _api.ScalarAsync<string>("SELECT status FROM article_revisions"));
        Assert.Equal(0, await _api.ScalarAsync<int>("SELECT submission_version FROM article_revisions"));
    }

    [Fact]
    public async Task Submitting_puts_the_revision_in_the_queue_and_stamps_the_submission()
    {
        var id = await PublishAsync();
        _api.Clock.Advance(TimeSpan.FromHours(2));

        var submitted = await SubmittedRevisionAsync(id);

        Assert.Equal("PendingReview", submitted.GetProperty("status").GetString());
        Assert.Equal(1, submitted.GetProperty("submissionVersion").GetInt32());
        Assert.Equal(_api.Clock.GetUtcNow(), submitted.GetProperty("submittedAt").GetDateTimeOffset());
        Assert.Equal(["withdraw"], submitted.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("Published", (await ArticleAsync(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Withdrawing_returns_the_revision_to_draft_and_it_can_be_resubmitted()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());

        var withdrawn = await OkAsync(await RevisionActionRawAsync(submitted, "withdraw"));
        var again = await RevisionActionRawAsync(withdrawn, "withdraw");
        var resubmitted = await OkAsync(await RevisionActionRawAsync(withdrawn, "submit"));

        Assert.Equal("Draft", withdrawn.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(2, resubmitted.GetProperty("submissionVersion").GetInt32());
    }

    [Fact]
    public async Task A_draft_revision_can_be_discarded_and_a_new_one_started()
    {
        var id = await PublishAsync();
        var revision = await CreateRevisionAsync(id);

        var delete = await _api.SendAsync(
            HttpMethod.Delete, $"/api/revisions/{revision.GetProperty("id").GetGuid()}?version={revision.GetProperty("version").GetUInt32()}", _annToken);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal("Published", (await ArticleAsync(id)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Created, (await CreateRevisionRawAsync(id)).StatusCode);
    }

    [Fact]
    public async Task Discarding_needs_the_current_version()
    {
        var revision = await CreateRevisionAsync(await PublishAsync());
        await EditAsync(revision);

        var stale = await _api.SendAsync(
            HttpMethod.Delete, $"/api/revisions/{revision.GetProperty("id").GetGuid()}?version={revision.GetProperty("version").GetUInt32()}", _annToken);
        var missing = await _api.SendAsync(HttpMethod.Delete, $"/api/revisions/{revision.GetProperty("id").GetGuid()}", _annToken);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM article_revisions"));
    }

    [Fact]
    public async Task The_revisions_of_an_article_are_listed_newest_first()
    {
        var id = await PublishAsync();
        var first = await SubmittedRevisionAsync(id, "First revision");
        await OkAsync(await ApproveRawAsync(first));
        _api.Clock.Advance(TimeSpan.FromMinutes(5));
        await CreateRevisionAsync(id);

        var list = await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}/revisions", _annToken));

        var statuses = list.EnumerateArray().Select(r => r.GetProperty("status").GetString()).ToList();
        Assert.Equal(["Draft", "Approved"], statuses);
    }

    // ---- the admin queue and the review ----

    [Fact]
    public async Task The_queue_lists_revisions_and_submissions_and_tells_them_apart()
    {
        var published = await PublishAsync("Already published");
        _api.Clock.Advance(TimeSpan.FromMinutes(1));
        var revision = await SubmittedRevisionAsync(published, "Revised title for the queue");
        _api.Clock.Advance(TimeSpan.FromMinutes(1));
        var fresh = await OkAsync(await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", _annToken, new { title = "Brand new article", summary = Summary, body = Body, categoryId = CategorySeed.Science.Id }));
        await OkAsync(await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{fresh.GetProperty("id").GetGuid()}/submit", _annToken, new { version = fresh.GetProperty("version").GetUInt32() }));

        var queue = await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/admin/reviews/queue", _adminToken));

        Assert.Equal(2, queue.GetProperty("totalCount").GetInt32());
        var items = queue.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("Revision", items[0].GetProperty("kind").GetString());
        Assert.Equal(revision.GetProperty("id").GetGuid(), items[0].GetProperty("revisionId").GetGuid());
        Assert.Equal(published, items[0].GetProperty("articleId").GetGuid());
        Assert.Equal("Revised title for the queue", items[0].GetProperty("title").GetString());
        Assert.Equal("Technology", items[0].GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("Submission", items[1].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("revisionId").ValueKind);
    }

    [Fact]
    public async Task The_queue_filters_and_pages_revisions_with_the_same_rules_as_submissions()
    {
        var annArticle = await PublishAsync("Ann's article");
        var bobArticle = await PublishAsync("Bob's article", _bobToken);
        await SubmittedRevisionAsync(annArticle, "Ann's revision");
        var bobRevision = await OkAsync(await CreateRevisionRawAsync(bobArticle, _bobToken));
        var bobEdited = await OkAsync(await EditRawAsync(bobRevision, "Bob's revision", token: _bobToken));
        await OkAsync(await RevisionActionRawAsync(bobEdited, "submit", _bobToken));

        var byAnn = await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/admin/reviews/queue?authorId={_ann.Id}", _adminToken));
        var byCategory = await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/admin/reviews/queue?categoryId={CategorySeed.Technology.Id}", _adminToken));
        var otherCategory = await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/admin/reviews/queue?categoryId={CategorySeed.Health.Id}", _adminToken));
        var paged = await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/admin/reviews/queue?pageSize=1&page=1", _adminToken));

        Assert.Equal(1, byAnn.GetProperty("totalCount").GetInt32());
        Assert.Equal("Ann's revision", byAnn.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal(2, byCategory.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, otherCategory.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, paged.GetProperty("totalCount").GetInt32());
        Assert.Single(paged.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Pending_revisions_that_are_withdrawn_or_decided_leave_the_queue()
    {
        var id = await PublishAsync();
        var submitted = await SubmittedRevisionAsync(id);
        var withdrawn = await OkAsync(await RevisionActionRawAsync(submitted, "withdraw"));

        var afterWithdraw = await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/admin/reviews/queue", _adminToken));
        var resubmitted = await OkAsync(await RevisionActionRawAsync(withdrawn, "submit"));
        await OkAsync(await ApproveRawAsync(resubmitted));
        var afterApprove = await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/admin/reviews/queue", _adminToken));

        Assert.Equal(0, afterWithdraw.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, afterApprove.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_administrator_sees_the_revision_next_to_the_published_version()
    {
        var id = await PublishAsync("Published title");
        var submitted = await SubmittedRevisionAsync(id, "Revised title");

        var detail = await OkAsync(await _api.SendAsync(
            HttpMethod.Get, $"/api/admin/revisions/{submitted.GetProperty("id").GetGuid()}", _adminToken));

        Assert.Equal("Revised title", detail.GetProperty("title").GetString());
        Assert.Equal(NewBody, detail.GetProperty("body").GetString());
        Assert.Equal("PendingReview", detail.GetProperty("status").GetString());
        Assert.True(detail.GetProperty("canReview").GetBoolean());
        var published = detail.GetProperty("published");
        Assert.Equal("Published title", published.GetProperty("title").GetString());
        Assert.Equal(Body, published.GetProperty("body").GetString());
        Assert.Equal(1, published.GetProperty("versionNumber").GetInt32());
        Assert.Equal("Ann Author", detail.GetProperty("author").GetProperty("fullName").GetString());
        Assert.Empty(detail.GetProperty("reviewHistory").EnumerateArray());
    }

    [Fact]
    public async Task A_revision_that_was_never_submitted_is_private()
    {
        var revision = await CreateRevisionAsync(await PublishAsync());

        var response = await _api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{revision.GetProperty("id").GetGuid()}", _adminToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{Guid.NewGuid()}", _adminToken)).StatusCode);
    }

    [Fact]
    public async Task Review_endpoints_for_revisions_are_for_administrators_only()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());
        var id = submitted.GetProperty("id").GetGuid();
        var body = new { version = submitted.GetProperty("version").GetUInt32(), submissionVersion = 1, reason = Reason };

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{id}", _annToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/revisions/{id}/approve", _annToken, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/revisions/{id}/reject", _annToken, body)).StatusCode);
        Assert.Equal("PendingReview", await _api.ScalarAsync<string>("SELECT status FROM article_revisions WHERE id = @id", ("id", id)));
    }

    // ---- approving ----

    [Fact]
    public async Task Approving_replaces_the_published_content_and_keeps_the_previous_version()
    {
        var id = await PublishAsync("Original title");
        var firstPublishedAt = (await ArticleAsync(id)).GetProperty("publishedAt").GetDateTimeOffset();
        var submitted = await SubmittedRevisionAsync(id, "Better title");
        _api.Clock.Advance(TimeSpan.FromHours(3));

        var response = await ApproveRawAsync(submitted);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadAsync(response);
        Assert.Equal("Approved", detail.GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canReview").GetBoolean());
        Assert.Equal("Better title", detail.GetProperty("published").GetProperty("title").GetString());
        Assert.Equal(2, detail.GetProperty("published").GetProperty("versionNumber").GetInt32());
        var entry = Assert.Single(detail.GetProperty("reviewHistory").EnumerateArray());
        Assert.Equal("Approved", entry.GetProperty("decision").GetString());
        Assert.Equal(_admin.Id, entry.GetProperty("reviewer").GetProperty("id").GetGuid());

        var article = await ArticleAsync(id);
        Assert.Equal("Published", article.GetProperty("status").GetString());
        Assert.Equal("Better title", article.GetProperty("title").GetString());
        Assert.Equal(NewSummary, article.GetProperty("summary").GetString());
        Assert.Equal(NewBody, article.GetProperty("body").GetString());
        Assert.Equal(2, article.GetProperty("publishedVersion").GetInt32());
        Assert.Equal(firstPublishedAt, article.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("openRevision").ValueKind);
        Assert.Equal(["revise"], article.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));

        var feed = await FeedItemAsync(id);
        Assert.Equal("Better title", feed.GetProperty("title").GetString());
        Assert.Equal(NewBody, feed.GetProperty("body").GetString());
        Assert.Equal(CategorySeed.Technology.Id, feed.GetProperty("category").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_replaced_version_is_kept_in_the_history()
    {
        var id = await PublishAsync("Original title");
        var publishedAt = await _api.ScalarAsync<DateTime>("SELECT published_at FROM articles WHERE id = @id", ("id", id));
        var submitted = await SubmittedRevisionAsync(id, "Better title");
        _api.Clock.Advance(TimeSpan.FromHours(3));

        await OkAsync(await ApproveRawAsync(submitted));

        Assert.Equal(1, await CountAsync("SELECT count(*) FROM article_published_versions WHERE article_id = @id", ("id", id)));
        Assert.Equal("Original title", await _api.ScalarAsync<string>("SELECT title FROM article_published_versions WHERE article_id = @id", ("id", id)));
        Assert.Equal(Body, await _api.ScalarAsync<string>("SELECT body FROM article_published_versions WHERE article_id = @id", ("id", id)));
        Assert.Equal(1, await _api.ScalarAsync<int>("SELECT version_number FROM article_published_versions WHERE article_id = @id", ("id", id)));
        Assert.Equal(publishedAt, await _api.ScalarAsync<DateTime>("SELECT published_at FROM article_published_versions WHERE article_id = @id", ("id", id)));
        Assert.Equal(
            submitted.GetProperty("id").GetGuid().ToString(),
            await _api.ScalarAsync<string>("SELECT superseded_by_revision_id::text FROM article_published_versions WHERE article_id = @id", ("id", id)));
    }

    [Fact]
    public async Task An_article_can_be_revised_again_after_an_approval_and_every_version_is_kept()
    {
        var id = await PublishAsync("Version one");
        await OkAsync(await ApproveRawAsync(await SubmittedRevisionAsync(id, "Version two")));
        await OkAsync(await ApproveRawAsync(await SubmittedRevisionAsync(id, "Version three")));

        var titles = new List<string>();
        await using var connection = new NpgsqlConnection(_api.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT title FROM article_published_versions WHERE article_id = @id ORDER BY version_number", connection);
        command.Parameters.AddWithValue("id", id);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                titles.Add(reader.GetString(0));
            }
        }

        Assert.Equal(["Version one", "Version two"], titles);
        Assert.Equal("Version three", (await FeedItemAsync(id)).GetProperty("title").GetString());
        Assert.Equal(3, (await ArticleAsync(id)).GetProperty("publishedVersion").GetInt32());
        Assert.Equal(2, await CountAsync("SELECT count(*) FROM article_revisions WHERE article_id = @id AND status = 'Approved'", ("id", id)));
    }

    [Fact]
    public async Task An_approved_revision_is_final()
    {
        var id = await PublishAsync();
        var submitted = await SubmittedRevisionAsync(id);
        await OkAsync(await ApproveRawAsync(submitted));
        var approved = await RevisionAsync(submitted);

        var edit = await EditRawAsync(approved, "Too late");
        var withdraw = await RevisionActionRawAsync(approved, "withdraw");
        var submitAgain = await RevisionActionRawAsync(approved, "submit");
        var delete = await _api.SendAsync(
            HttpMethod.Delete, $"/api/revisions/{approved.GetProperty("id").GetGuid()}?version={approved.GetProperty("version").GetUInt32()}", _annToken);

        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Empty(approved.GetProperty("allowedActions").EnumerateArray());
        Assert.All(new[] { edit, withdraw, submitAgain, delete }, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Deciding_twice_on_the_same_submission_is_a_conflict()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());
        await OkAsync(await ApproveRawAsync(submitted));

        var again = await ApproveRawAsync(submitted);
        var reject = await RejectRawAsync(submitted);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        var problem = await ReadAsync(again);
        Assert.Equal("Approved", problem.GetProperty("currentStatus").GetString());
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM revision_reviews"));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM article_published_versions"));
    }

    [Fact]
    public async Task A_decision_on_a_stale_version_or_an_old_submission_is_refused()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());

        var staleVersion = await ApproveRawAsync(submitted, version: submitted.GetProperty("version").GetUInt32() + 1);
        var oldSubmission = await ApproveRawAsync(submitted, submissionVersion: 2);

        Assert.Equal(HttpStatusCode.Conflict, staleVersion.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, oldSubmission.StatusCode);
        Assert.Contains("newer version", (await ReadAsync(oldSubmission)).GetProperty("detail").GetString());
        Assert.Equal("PendingReview", await _api.ScalarAsync<string>("SELECT status FROM article_revisions"));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM revision_reviews"));
    }

    [Fact]
    public async Task Approving_needs_the_version_and_the_submission_in_the_request()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());

        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/revisions/{submitted.GetProperty("id").GetGuid()}/approve", _adminToken, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approving_an_unknown_revision_is_not_found()
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/revisions/{Guid.NewGuid()}/approve", _adminToken, new { version = 1u, submissionVersion = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- rejecting ----

    [Fact]
    public async Task Rejecting_leaves_the_published_version_untouched_and_the_author_sees_the_reason()
    {
        var id = await PublishAsync("Published title");
        var submitted = await SubmittedRevisionAsync(id, "Rejected title");

        var response = await RejectRawAsync(submitted, $"   {Reason}   ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadAsync(response);
        Assert.Equal("Rejected", detail.GetProperty("status").GetString());
        Assert.Equal("Published title", detail.GetProperty("published").GetProperty("title").GetString());

        var feed = await FeedItemAsync(id);
        Assert.Equal("Published title", feed.GetProperty("title").GetString());
        Assert.Equal(Body, feed.GetProperty("body").GetString());
        Assert.Equal(1, (await ArticleAsync(id)).GetProperty("publishedVersion").GetInt32());
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM article_published_versions"));

        var revision = await RevisionAsync(submitted);
        Assert.Equal("Rejected", revision.GetProperty("status").GetString());
        var latest = revision.GetProperty("latestReview");
        Assert.Equal("Rejected", latest.GetProperty("decision").GetString());
        Assert.Equal(Reason, latest.GetProperty("reason").GetString());
        var review = Assert.Single(revision.GetProperty("reviews").EnumerateArray());
        Assert.False(review.TryGetProperty("reviewer", out _)); // authors never see who decided
        Assert.Equal(["edit", "delete", "submit"], revision.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too short")]
    public async Task A_rejection_needs_a_reason_of_at_least_ten_characters(string? reason)
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());

        var response = await RejectRawAsync(submitted, reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("reason", out _));
        Assert.Equal("PendingReview", await _api.ScalarAsync<string>("SELECT status FROM article_revisions"));
    }

    [Fact]
    public async Task A_reason_of_more_than_1000_characters_is_refused()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await RejectRawAsync(submitted, new string('x', 1001))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RejectRawAsync(submitted, new string('x', 1000))).StatusCode);
    }

    [Fact]
    public async Task A_rejected_revision_can_be_edited_and_resubmitted_and_earlier_decisions_are_kept()
    {
        var id = await PublishAsync("Published title");
        var submitted = await SubmittedRevisionAsync(id, "First attempt");
        await OkAsync(await RejectRawAsync(submitted));
        var rejected = await RevisionAsync(submitted);

        var edited = await EditAsync(rejected, "Second attempt");
        var resubmitted = await OkAsync(await RevisionActionRawAsync(edited, "submit"));
        await OkAsync(await ApproveRawAsync(resubmitted));

        Assert.Equal(2, resubmitted.GetProperty("submissionVersion").GetInt32());
        var final = await RevisionAsync(submitted);
        var decisions = final.GetProperty("reviews").EnumerateArray().Select(r => r.GetProperty("decision").GetString()).ToList();
        Assert.Equal(["Approved", "Rejected"], decisions);
        Assert.Equal("Second attempt", (await FeedItemAsync(id)).GetProperty("title").GetString());

        var admin = await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{submitted.GetProperty("id").GetGuid()}", _adminToken));
        Assert.Equal(2, admin.GetProperty("reviewHistory").GetArrayLength());
    }

    [Fact]
    public async Task A_rejected_revision_can_be_discarded_and_replaced_by_a_new_one()
    {
        var id = await PublishAsync();
        var submitted = await SubmittedRevisionAsync(id);
        await OkAsync(await RejectRawAsync(submitted));
        var rejected = await RevisionAsync(submitted);

        var delete = await _api.SendAsync(
            HttpMethod.Delete, $"/api/revisions/{rejected.GetProperty("id").GetGuid()}?version={rejected.GetProperty("version").GetUInt32()}", _annToken);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM revision_reviews"));
        Assert.Equal(HttpStatusCode.Created, (await CreateRevisionRawAsync(id)).StatusCode);
    }

    // ---- the feed ----

    [Fact]
    public async Task Draft_pending_and_rejected_revisions_never_appear_in_the_feed()
    {
        var id = await PublishAsync("Live title");

        async Task AssertFeedStillShowsLiveAsync()
        {
            var list = await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/feed", _bobToken));
            var item = Assert.Single(list.GetProperty("items").EnumerateArray());
            Assert.Equal("Live title", item.GetProperty("title").GetString());
            Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
            Assert.Equal("Live title", (await FeedItemAsync(id)).GetProperty("title").GetString());
        }

        var revision = await CreateRevisionAsync(id);
        await AssertFeedStillShowsLiveAsync();

        var edited = await EditAsync(revision, "Draft revision title");
        var submitted = await OkAsync(await RevisionActionRawAsync(edited, "submit"));
        await AssertFeedStillShowsLiveAsync();

        await OkAsync(await RejectRawAsync(submitted));
        await AssertFeedStillShowsLiveAsync();
    }

    [Fact]
    public async Task The_feed_lists_an_article_once_with_its_latest_approved_version()
    {
        var id = await PublishAsync("Version one");
        await OkAsync(await ApproveRawAsync(await SubmittedRevisionAsync(id, "Version two")));

        var list = await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/feed?search=Version", _bobToken));

        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("Version two", item.GetProperty("title").GetString());
        Assert.Equal(CategorySeed.Technology.Id, item.GetProperty("category").GetProperty("id").GetGuid());
        Assert.Empty((await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/feed?search=one", _bobToken))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task The_feed_filters_by_the_category_of_the_current_version()
    {
        var id = await PublishAsync("Moves category");
        await OkAsync(await ApproveRawAsync(await SubmittedRevisionAsync(id)));

        var science = await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/feed?categoryId={CategorySeed.Science.Id}", _bobToken));
        var technology = await OkAsync(await _api.SendAsync(HttpMethod.Get, $"/api/feed?categoryId={CategorySeed.Technology.Id}", _bobToken));

        Assert.Equal(0, science.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, technology.GetProperty("totalCount").GetInt32());
    }

    // ---- suspended authors ----

    [Fact]
    public async Task Review_actions_are_blocked_for_revisions_of_a_suspended_author()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());
        await SuspendAnnAsync();

        var approve = await ApproveRawAsync(submitted);
        var reject = await RejectRawAsync(submitted);
        var detail = await OkAsync(await _api.SendAsync(
            HttpMethod.Get, $"/api/admin/revisions/{submitted.GetProperty("id").GetGuid()}", _adminToken));

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Contains("suspended", (await ReadAsync(approve)).GetProperty("detail").GetString());
        Assert.False(detail.GetProperty("canReview").GetBoolean());
        Assert.Contains("suspended", detail.GetProperty("reviewBlockedReason").GetString());
        Assert.Equal("Suspended", detail.GetProperty("author").GetProperty("status").GetString());
        Assert.Equal("PendingReview", await _api.ScalarAsync<string>("SELECT status FROM article_revisions"));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM revision_reviews"));
    }

    [Fact]
    public async Task The_published_article_of_a_suspended_author_stays_in_the_feed()
    {
        var id = await PublishAsync("Still readable");
        await SuspendAnnAsync();

        Assert.Equal("Still readable", (await FeedItemAsync(id)).GetProperty("title").GetString());
    }

    // ---- author dashboard ----

    [Fact]
    public async Task My_articles_shows_the_revision_status_next_to_the_published_article()
    {
        var id = await PublishAsync("Dashboard article");

        async Task<JsonElement> ItemAsync() =>
            (await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/me/articles", _annToken)))
            .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);

        var before = await ItemAsync();
        Assert.Equal(JsonValueKind.Null, before.GetProperty("revision").ValueKind);
        Assert.Equal(["revise"], before.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));

        var revision = await CreateRevisionAsync(id);
        var draft = await ItemAsync();
        Assert.Equal("Draft", draft.GetProperty("revision").GetProperty("status").GetString());
        Assert.Equal(revision.GetProperty("id").GetGuid(), draft.GetProperty("revision").GetProperty("id").GetGuid());
        Assert.Empty(draft.GetProperty("allowedActions").EnumerateArray());

        var edited = await EditAsync(revision);
        await OkAsync(await RevisionActionRawAsync(edited, "submit"));
        Assert.Equal("PendingReview", (await ItemAsync()).GetProperty("revision").GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_status_counts_still_count_articles_not_revisions()
    {
        var id = await PublishAsync();
        await SubmittedRevisionAsync(id);

        var stats = await OkAsync(await _api.SendAsync(HttpMethod.Get, "/api/me/articles/stats", _annToken));

        Assert.Equal(1, stats.GetProperty("published").GetInt32());
        Assert.Equal(0, stats.GetProperty("pendingReview").GetInt32());
    }

    // ---- races ----

    [Fact]
    public async Task Simultaneous_approvals_let_exactly_one_succeed_and_publish_once()
    {
        for (var round = 0; round < 6; round++)
        {
            var id = await PublishAsync($"Approve race {round}");
            var submitted = await SubmittedRevisionAsync(id, $"Approved race {round}");

            var responses = await Task.WhenAll(ApproveRawAsync(submitted), ApproveRawAsync(submitted), ApproveRawAsync(submitted));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await CountAsync("SELECT count(*) FROM revision_reviews WHERE revision_id = @id", ("id", submitted.GetProperty("id").GetGuid())));
            Assert.Equal(1, await CountAsync("SELECT count(*) FROM article_published_versions WHERE article_id = @id", ("id", id)));
            Assert.Equal(2, await _api.ScalarAsync<int>("SELECT published_version FROM articles WHERE id = @id", ("id", id)));
        }
    }

    [Fact]
    public async Task An_approval_and_a_rejection_at_the_same_moment_let_exactly_one_win()
    {
        for (var round = 0; round < 6; round++)
        {
            var id = await PublishAsync($"Mixed race {round}");
            var submitted = await SubmittedRevisionAsync(id, $"Mixed revision {round}");

            var responses = await Task.WhenAll(ApproveRawAsync(submitted), RejectRawAsync(submitted));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await CountAsync("SELECT count(*) FROM revision_reviews WHERE revision_id = @id", ("id", submitted.GetProperty("id").GetGuid())));
            var published = await _api.ScalarAsync<int>("SELECT published_version FROM articles WHERE id = @id", ("id", id));
            var status = await _api.ScalarAsync<string>("SELECT status FROM article_revisions WHERE id = @id", ("id", submitted.GetProperty("id").GetGuid()));
            Assert.Equal(status == "Approved" ? 2 : 1, published);
        }
    }

    [Fact]
    public async Task A_decision_racing_the_authors_withdrawal_lets_exactly_one_succeed()
    {
        for (var round = 0; round < 10; round++)
        {
            var id = await PublishAsync($"Withdraw race {round}");
            var submitted = await SubmittedRevisionAsync(id, $"Withdraw revision {round}");
            var revisionId = submitted.GetProperty("id").GetGuid();

            var responses = await Task.WhenAll(ApproveRawAsync(submitted), RevisionActionRawAsync(submitted, "withdraw"));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            if (responses[0].StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal("Approved", await _api.ScalarAsync<string>("SELECT status FROM article_revisions WHERE id = @id", ("id", revisionId)));
                Assert.Equal(2, await _api.ScalarAsync<int>("SELECT published_version FROM articles WHERE id = @id", ("id", id)));
            }
            else
            {
                Assert.Equal("Draft", await _api.ScalarAsync<string>("SELECT status FROM article_revisions WHERE id = @id", ("id", revisionId)));
                Assert.Equal(1, await _api.ScalarAsync<int>("SELECT published_version FROM articles WHERE id = @id", ("id", id)));
                Assert.Equal(0, await CountAsync("SELECT count(*) FROM revision_reviews WHERE revision_id = @id", ("id", revisionId)));
            }
        }
    }

    [Fact]
    public async Task The_unique_review_per_revision_submission_is_the_last_line_of_defence()
    {
        var submitted = await SubmittedRevisionAsync(await PublishAsync());
        await OkAsync(await RejectRawAsync(submitted));

        var ex = await Assert.ThrowsAsync<PostgresException>(() => _api.ExecuteAsync(
            """
            INSERT INTO revision_reviews (id, revision_id, submission_version, reviewer_id, decision, reason, decided_at)
            SELECT gen_random_uuid(), revision_id, submission_version, reviewer_id, 'Approved', NULL, now() FROM revision_reviews
            """));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
    }

    // ---- migration ----

    [Fact]
    public async Task Articles_published_before_revisions_existed_are_backfilled_as_version_one()
    {
        var id = await PublishAsync();

        // The migration set these for rows that were already published; a fresh approval sets them the same way.
        Assert.Equal(1, await _api.ScalarAsync<int>("SELECT published_version FROM articles WHERE id = @id", ("id", id)));
        Assert.Equal(
            await _api.ScalarAsync<DateTime>("SELECT published_at FROM articles WHERE id = @id", ("id", id)),
            await _api.ScalarAsync<DateTime>("SELECT current_version_published_at FROM articles WHERE id = @id", ("id", id)));
    }
}
