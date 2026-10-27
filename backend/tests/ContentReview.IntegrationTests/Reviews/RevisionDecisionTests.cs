using System.Net;
using System.Text.Json;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Reviews;

[Collection(PostgresCollection.Name)]
public class RevisionDecisionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private RevisionScenario _s = null!;

    public async Task InitializeAsync() => _s = await RevisionScenario.CreateAsync(postgres);

    public Task DisposeAsync() => _s.DisposeAsync().AsTask();

    private static Guid IdOf(JsonElement element) => element.GetProperty("id").GetGuid();

    private async Task<JsonElement> AdminGetAsync(Guid revisionId)
    {
        var response = await _s.Api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{revisionId}", _s.AdminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await RevisionScenario.ReadAsync(response);
    }

    private async Task<JsonElement> FeedItemAsync(Guid articleId)
    {
        var response = await _s.Api.SendAsync(HttpMethod.Get, $"/api/feed/{articleId}", _s.BobToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await RevisionScenario.ReadAsync(response);
    }

    // ---- detail ----

    [Fact]
    public async Task The_detail_shows_the_revision_next_to_the_published_version()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");

        var detail = await AdminGetAsync(IdOf(pending));

        Assert.Equal("Revised title", detail.GetProperty("title").GetString());
        Assert.Equal("PendingReview", detail.GetProperty("status").GetString());
        Assert.Equal(1, detail.GetProperty("submissionVersion").GetInt32());
        Assert.Equal(1, detail.GetProperty("number").GetInt32());
        Assert.Equal(articleId, detail.GetProperty("articleId").GetGuid());
        Assert.True(detail.GetProperty("canReview").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("reviewBlockedReason").ValueKind);
        var published = detail.GetProperty("published");
        Assert.Equal("Original title", published.GetProperty("title").GetString());
        Assert.Equal(RevisionScenario.Body, published.GetProperty("body").GetString());
        Assert.Equal(1, published.GetProperty("publishedVersion").GetInt32());
        Assert.Equal("Ann Author", detail.GetProperty("author").GetProperty("fullName").GetString());
        Assert.Empty(detail.GetProperty("reviewHistory").EnumerateArray());
    }

    [Fact]
    public async Task A_revision_that_was_never_submitted_is_a_private_draft_and_unknown_ids_are_404()
    {
        var articleId = await _s.PublishedArticleAsync();
        var draft = await _s.DraftRevisionAsync(articleId);

        var response = await _s.Api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{IdOf(draft)}", _s.AdminToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _s.Api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{Guid.NewGuid()}", _s.AdminToken)).StatusCode);
    }

    [Fact]
    public async Task Authors_and_anonymous_callers_cannot_use_the_admin_revision_endpoints()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId);
        var id = IdOf(pending);

        Assert.Equal(HttpStatusCode.Forbidden, (await _s.Api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{id}", _s.AnnToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _s.ApproveRevisionAsync(id, RevisionScenario.VersionOf(pending), 1, _s.AnnToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _s.RejectRevisionAsync(id, RevisionScenario.VersionOf(pending), 1, token: _s.AnnToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _s.Api.SendAsync(HttpMethod.Get, $"/api/admin/revisions/{id}", null)).StatusCode);
    }

    // ---- queue ----

    [Fact]
    public async Task The_queue_lists_revisions_next_to_new_submissions_and_tells_them_apart()
    {
        var articleId = await _s.PublishedArticleAsync("Published one");
        var pending = await _s.PendingRevisionAsync(articleId, "Revision of published one");

        var created = await RevisionScenario.ReadAsync(await _s.Api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", _s.BobToken,
            new { title = "A brand new article", summary = RevisionScenario.Summary, body = RevisionScenario.Body, categoryId = ContentReview.Infrastructure.Persistence.CategorySeed.Science.Id }));
        var newId = IdOf(created);
        await _s.Api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{newId}/submit", _s.BobToken, new { version = RevisionScenario.VersionOf(created) });

        var response = await _s.Api.SendAsync(HttpMethod.Get, "/api/admin/reviews/queue", _s.AdminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var queue = await RevisionScenario.ReadAsync(response);
        Assert.Equal(2, queue.GetProperty("totalCount").GetInt32());
        var items = queue.GetProperty("items").EnumerateArray().ToList();
        var revision = Assert.Single(items, i => i.GetProperty("kind").GetString() == "Revision");
        Assert.Equal(IdOf(pending), revision.GetProperty("revisionId").GetGuid());
        Assert.Equal(articleId, revision.GetProperty("articleId").GetGuid());
        Assert.Equal("Revision of published one", revision.GetProperty("title").GetString());
        var article = Assert.Single(items, i => i.GetProperty("kind").GetString() == "Article");
        Assert.Equal(newId, article.GetProperty("articleId").GetGuid());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("revisionId").ValueKind);
    }

    [Fact]
    public async Task The_queue_filters_and_pages_over_both_kinds()
    {
        var a = await _s.PublishedArticleAsync("Ann published");
        await _s.PendingRevisionAsync(a, "Ann revision");
        var b = await _s.PublishedArticleAsync("Bob published", _s.BobToken);
        var bobRevision = await RevisionScenario.ReadAsync(await _s.CreateRevisionAsync(b, _s.BobToken));
        var edited = await RevisionScenario.ReadAsync(await _s.UpdateRevisionAsync(b, new
        {
            title = "Bob revision",
            summary = "A revised summary of the article.",
            body = new string('r', 150),
            categoryId = ContentReview.Infrastructure.Persistence.CategorySeed.Science.Id,
            version = RevisionScenario.VersionOf(bobRevision),
        }, _s.BobToken));
        await _s.SubmitRevisionAsync(b, RevisionScenario.VersionOf(edited), _s.BobToken);

        var byBob = await RevisionScenario.ReadAsync(await _s.Api.SendAsync(
            HttpMethod.Get, $"/api/admin/reviews/queue?authorId={_s.Bob.Id}", _s.AdminToken));
        Assert.Equal(1, byBob.GetProperty("totalCount").GetInt32());
        Assert.Equal("Bob revision", byBob.GetProperty("items")[0].GetProperty("title").GetString());

        var paged = await RevisionScenario.ReadAsync(await _s.Api.SendAsync(
            HttpMethod.Get, "/api/admin/reviews/queue?pageSize=1&page=2", _s.AdminToken));
        Assert.Equal(2, paged.GetProperty("totalCount").GetInt32());
        Assert.Single(paged.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_decided_revision_leaves_the_queue()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId);
        await _s.RejectRevisionAsync(IdOf(pending), RevisionScenario.VersionOf(pending), 1);

        var queue = await RevisionScenario.ReadAsync(await _s.Api.SendAsync(HttpMethod.Get, "/api/admin/reviews/queue", _s.AdminToken));

        Assert.Equal(0, queue.GetProperty("totalCount").GetInt32());
    }

    // ---- approve ----

    [Fact]
    public async Task Approving_replaces_the_published_content_and_keeps_the_replaced_version_in_history()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");
        var before = await _s.GetArticleAsync(articleId);
        _s.Api.Clock.Advance(TimeSpan.FromHours(1));

        var response = await _s.ApproveRevisionAsync(IdOf(pending), RevisionScenario.VersionOf(pending), 1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await RevisionScenario.ReadAsync(response);
        Assert.Equal("Approved", detail.GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canReview").GetBoolean());
        Assert.Equal("Revised title", detail.GetProperty("published").GetProperty("title").GetString());
        Assert.Equal(2, detail.GetProperty("published").GetProperty("publishedVersion").GetInt32());
        var history = Assert.Single(detail.GetProperty("reviewHistory").EnumerateArray());
        Assert.Equal("Approved", history.GetProperty("decision").GetString());
        Assert.Equal("Alice Admin", history.GetProperty("reviewer").GetProperty("fullName").GetString());

        var article = await _s.GetArticleAsync(articleId);
        Assert.Equal("Published", article.GetProperty("status").GetString());
        Assert.Equal("Revised title", article.GetProperty("title").GetString());
        Assert.Equal(new string('r', 150), article.GetProperty("body").GetString());
        Assert.Equal(2, article.GetProperty("publishedVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("openRevision").ValueKind);
        Assert.Contains("revise", article.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        Assert.NotEqual(
            before.GetProperty("publishedAt").GetDateTimeOffset(), article.GetProperty("publishedAt").GetDateTimeOffset());

        Assert.Equal(1L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM article_published_versions WHERE article_id = @id", ("id", articleId)));
        Assert.Equal("Original title", await _s.Api.ScalarAsync<string>("SELECT title FROM article_published_versions WHERE article_id = @id AND version = 1", ("id", articleId)));
    }

    [Fact]
    public async Task The_feed_shows_the_published_version_until_the_revision_is_approved()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");

        Assert.Equal("Original title", (await FeedItemAsync(articleId)).GetProperty("title").GetString());

        await _s.ApproveRevisionAsync(IdOf(pending), RevisionScenario.VersionOf(pending), 1);

        var feed = await FeedItemAsync(articleId);
        Assert.Equal("Revised title", feed.GetProperty("title").GetString());
        Assert.Equal(new string('r', 150), feed.GetProperty("body").GetString());
    }

    [Fact]
    public async Task A_second_revision_can_follow_the_first_and_history_grows()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var first = await _s.PendingRevisionAsync(articleId, "Second title");
        await _s.ApproveRevisionAsync(IdOf(first), RevisionScenario.VersionOf(first), 1);

        var second = await _s.PendingRevisionAsync(articleId, "Third title");
        Assert.Equal(2, second.GetProperty("number").GetInt32());
        var response = await _s.ApproveRevisionAsync(IdOf(second), RevisionScenario.VersionOf(second), 1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var article = await _s.GetArticleAsync(articleId);
        Assert.Equal("Third title", article.GetProperty("title").GetString());
        Assert.Equal(3, article.GetProperty("publishedVersion").GetInt32());
        Assert.Equal(2L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM article_published_versions WHERE article_id = @id", ("id", articleId)));
    }

    // ---- reject ----

    [Fact]
    public async Task Rejecting_leaves_the_published_version_and_the_feed_unchanged_and_shows_the_author_the_reason()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");
        var before = await _s.GetArticleAsync(articleId);

        var response = await _s.RejectRevisionAsync(IdOf(pending), RevisionScenario.VersionOf(pending), 1, "The new introduction is not accurate.");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rejected", (await RevisionScenario.ReadAsync(response)).GetProperty("status").GetString());

        var article = await _s.GetArticleAsync(articleId);
        Assert.Equal("Original title", article.GetProperty("title").GetString());
        Assert.Equal(RevisionScenario.VersionOf(before), RevisionScenario.VersionOf(article));
        Assert.Equal("Rejected", article.GetProperty("openRevision").GetProperty("status").GetString());
        Assert.Equal("Original title", (await FeedItemAsync(articleId)).GetProperty("title").GetString());

        var revision = await RevisionScenario.ReadAsync(await _s.GetRevisionAsync(articleId));
        Assert.Equal("The new introduction is not accurate.", revision.GetProperty("latestReview").GetProperty("reason").GetString());
        Assert.Equal(["edit", "submit"], revision.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()!));

        var reviews = await RevisionScenario.ReadAsync(await _s.Api.SendAsync(HttpMethod.Get, $"/api/articles/{articleId}/revision/reviews", _s.AnnToken));
        var entry = Assert.Single(reviews.EnumerateArray());
        Assert.Equal("Rejected", entry.GetProperty("decision").GetString());
        Assert.False(entry.TryGetProperty("reviewer", out _));
    }

    [Fact]
    public async Task A_rejected_revision_can_be_reworked_resubmitted_and_approved_and_earlier_decisions_stay()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");
        var rejected = await RevisionScenario.ReadAsync(
            await _s.RejectRevisionAsync(IdOf(pending), RevisionScenario.VersionOf(pending), 1));

        var current = await RevisionScenario.ReadAsync(await _s.GetRevisionAsync(articleId));
        var edit = await _s.UpdateRevisionAsync(articleId, new
        {
            title = "Reworked title",
            summary = "A reworked summary of the article.",
            body = new string('w', 150),
            categoryId = ContentReview.Infrastructure.Persistence.CategorySeed.Science.Id,
            version = RevisionScenario.VersionOf(current),
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var resubmitted = await RevisionScenario.ReadAsync(
            await _s.SubmitRevisionAsync(articleId, RevisionScenario.VersionOf(await RevisionScenario.ReadAsync(edit))));
        Assert.Equal(2, resubmitted.GetProperty("submissionVersion").GetInt32());

        var approved = await _s.ApproveRevisionAsync(IdOf(rejected), RevisionScenario.VersionOf(resubmitted), 2);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var history = (await RevisionScenario.ReadAsync(approved)).GetProperty("reviewHistory").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(["Approved", "Rejected"], history.Select(h => h.GetProperty("decision").GetString()!));
        Assert.Equal("Reworked title", (await _s.GetArticleAsync(articleId)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_of_ten_to_a_thousand_characters()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId);
        var id = IdOf(pending);
        var version = RevisionScenario.VersionOf(pending);

        foreach (var reason in new string?[] { null, "   ", "too short", new string('x', 1001) })
        {
            var response = await _s.RejectRevisionAsync(id, version, 1, reason);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True((await RevisionScenario.ReadAsync(response)).GetProperty("errors").TryGetProperty("reason", out _));
        }

        Assert.Equal("PendingReview", (await AdminGetAsync(id)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _s.RejectRevisionAsync(id, version, 1, new string('x', 10))).StatusCode);
    }

    // ---- conflicts ----

    [Fact]
    public async Task A_revision_is_decided_once()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId);
        var id = IdOf(pending);
        var version = RevisionScenario.VersionOf(pending);
        await _s.ApproveRevisionAsync(id, version, 1);

        var again = await _s.ApproveRevisionAsync(id, version, 1);
        var reject = await _s.RejectRevisionAsync(id, version, 1);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        var problem = await RevisionScenario.ReadAsync(again);
        Assert.Equal("Approved", problem.GetProperty("currentStatus").GetString());
        Assert.True(problem.TryGetProperty("currentVersion", out _));
        Assert.Equal(1L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM revision_reviews WHERE revision_id = @id", ("id", id)));
        Assert.Equal(1L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM article_published_versions WHERE article_id = @id", ("id", articleId)));
    }

    [Fact]
    public async Task A_stale_version_or_a_newer_submission_is_a_conflict_that_says_what_is_current()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId);
        var id = IdOf(pending);
        var version = RevisionScenario.VersionOf(pending);

        var stale = await _s.ApproveRevisionAsync(id, version + 7, 1);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await RevisionScenario.ReadAsync(stale);
        Assert.Equal("PendingReview", problem.GetProperty("currentStatus").GetString());
        Assert.Equal(version, problem.GetProperty("currentVersion").GetUInt32());

        var newer = await _s.ApproveRevisionAsync(id, version, 2);
        Assert.Equal(HttpStatusCode.Conflict, newer.StatusCode);
        Assert.Contains("newer version", await newer.Content.ReadAsStringAsync());

        Assert.Equal("PendingReview", (await AdminGetAsync(id)).GetProperty("status").GetString());
        Assert.Equal(0L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM revision_reviews WHERE revision_id = @id", ("id", id)));
    }

    [Fact]
    public async Task A_withdrawn_revision_cannot_be_decided()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId);
        await _s.WithdrawRevisionAsync(articleId, RevisionScenario.VersionOf(pending));

        var response = await _s.ApproveRevisionAsync(IdOf(pending), RevisionScenario.VersionOf(pending), 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Draft", (await RevisionScenario.ReadAsync(response)).GetProperty("currentStatus").GetString());
    }

    [Fact]
    public async Task Review_is_blocked_for_a_suspended_author_and_nothing_changes()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");
        await _s.Api.ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE id = @id", ("id", _s.Ann.Id));

        var detail = await AdminGetAsync(IdOf(pending));
        Assert.False(detail.GetProperty("canReview").GetBoolean());
        Assert.Equal("Author account is suspended.", detail.GetProperty("reviewBlockedReason").GetString());

        var approve = await _s.ApproveRevisionAsync(IdOf(pending), RevisionScenario.VersionOf(pending), 1);
        var reject = await _s.RejectRevisionAsync(IdOf(pending), RevisionScenario.VersionOf(pending), 1);

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Contains("suspended", await approve.Content.ReadAsStringAsync());
        Assert.Equal("Original title", (await FeedItemAsync(articleId)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_decision_needs_a_version_and_a_submission_version()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId);
        var id = IdOf(pending);

        var none = await _s.Api.SendJsonAsync(HttpMethod.Post, $"/api/admin/revisions/{id}/approve", _s.AdminToken, new { });
        var zero = await _s.Api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/revisions/{id}/approve", _s.AdminToken,
            new { version = RevisionScenario.VersionOf(pending), submissionVersion = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    // ---- simultaneous decisions ----

    [Fact]
    public async Task Racing_approvals_by_several_administrators_publish_the_revision_exactly_once()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");
        var id = IdOf(pending);
        var version = RevisionScenario.VersionOf(pending);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _s.ApproveRevisionAsync(id, version, 1)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM revision_reviews WHERE revision_id = @id", ("id", id)));
        Assert.Equal(1L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM article_published_versions WHERE article_id = @id", ("id", articleId)));
        Assert.Equal(2, await _s.Api.ScalarAsync<int>("SELECT published_version FROM articles WHERE id = @id", ("id", articleId)));
        Assert.Equal("Revised title", (await FeedItemAsync(articleId)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_racing_approve_and_reject_record_exactly_one_decision()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");
        var id = IdOf(pending);
        var version = RevisionScenario.VersionOf(pending);

        var responses = await Task.WhenAll(
            _s.ApproveRevisionAsync(id, version, 1),
            _s.RejectRevisionAsync(id, version, 1),
            _s.ApproveRevisionAsync(id, version, 1),
            _s.RejectRevisionAsync(id, version, 1));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM revision_reviews WHERE revision_id = @id", ("id", id)));

        var approved = await _s.Api.ScalarAsync<string>("SELECT status FROM article_revisions WHERE id = @id", ("id", id)) == "Approved";
        var expectedTitle = approved ? "Revised title" : "Original title";
        Assert.Equal(expectedTitle, (await FeedItemAsync(articleId)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_racing_withdraw_and_approve_succeed_exactly_once_between_them()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var pending = await _s.PendingRevisionAsync(articleId, "Revised title");
        var version = RevisionScenario.VersionOf(pending);

        var responses = await Task.WhenAll(
            _s.ApproveRevisionAsync(IdOf(pending), version, 1),
            _s.WithdrawRevisionAsync(articleId, version));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var status = await _s.Api.ScalarAsync<string>("SELECT status FROM article_revisions WHERE id = @id", ("id", IdOf(pending)));
        Assert.Contains(status, new[] { "Approved", "Draft" });
        var reviews = await _s.Api.ScalarAsync<long>("SELECT count(*) FROM revision_reviews WHERE revision_id = @id", ("id", IdOf(pending)));
        Assert.Equal(status == "Approved" ? 1L : 0L, reviews);
    }

    [Fact]
    public async Task Revisions_of_different_articles_are_decided_independently()
    {
        var first = await _s.PublishedArticleAsync("First original");
        var second = await _s.PublishedArticleAsync("Second original", _s.BobToken);
        var firstPending = await _s.PendingRevisionAsync(first, "First revised");
        var secondDraft = await RevisionScenario.ReadAsync(await _s.CreateRevisionAsync(second, _s.BobToken));
        var secondEdited = await RevisionScenario.ReadAsync(await _s.UpdateRevisionAsync(second, new
        {
            title = "Second revised",
            summary = "A revised summary of the article.",
            body = new string('r', 150),
            categoryId = ContentReview.Infrastructure.Persistence.CategorySeed.Science.Id,
            version = RevisionScenario.VersionOf(secondDraft),
        }, _s.BobToken));
        var secondPending = await RevisionScenario.ReadAsync(
            await _s.SubmitRevisionAsync(second, RevisionScenario.VersionOf(secondEdited), _s.BobToken));

        var responses = await Task.WhenAll(
            _s.ApproveRevisionAsync(IdOf(firstPending), RevisionScenario.VersionOf(firstPending), 1),
            _s.ApproveRevisionAsync(IdOf(secondPending), RevisionScenario.VersionOf(secondPending), 1));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal("First revised", (await FeedItemAsync(first)).GetProperty("title").GetString());
        Assert.Equal("Second revised", (await FeedItemAsync(second)).GetProperty("title").GetString());
    }
}
