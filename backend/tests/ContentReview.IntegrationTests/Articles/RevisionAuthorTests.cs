using System.Net;
using System.Text.Json;
using ContentReview.IntegrationTests.Infrastructure;
using ContentReview.Infrastructure.Persistence;

namespace ContentReview.IntegrationTests.Articles;

[Collection(PostgresCollection.Name)]
public class RevisionAuthorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private RevisionScenario _s = null!;

    public async Task InitializeAsync() => _s = await RevisionScenario.CreateAsync(postgres);

    public Task DisposeAsync() => _s.DisposeAsync().AsTask();

    private static string[] ActionsOf(JsonElement element) =>
        element.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()!).ToArray();

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response) =>
        (await RevisionScenario.ReadAsync(response)).GetProperty("errors");

    // ---- create ----

    [Fact]
    public async Task Creating_a_revision_copies_the_published_content_into_a_draft_and_leaves_the_article_alone()
    {
        var id = await _s.PublishedArticleAsync("Original title");
        var before = await _s.GetArticleAsync(id);

        var response = await _s.CreateRevisionAsync(id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var revision = await RevisionScenario.ReadAsync(response);
        Assert.Equal("Draft", revision.GetProperty("status").GetString());
        Assert.Equal(1, revision.GetProperty("number").GetInt32());
        Assert.Equal(0, revision.GetProperty("submissionVersion").GetInt32());
        Assert.Equal("Original title", revision.GetProperty("title").GetString());
        Assert.Equal(RevisionScenario.Summary, revision.GetProperty("summary").GetString());
        Assert.Equal(RevisionScenario.Body, revision.GetProperty("body").GetString());
        Assert.Equal(CategorySeed.Science.Id, revision.GetProperty("category").GetProperty("id").GetGuid());
        Assert.Equal(["edit", "submit"], ActionsOf(revision));
        Assert.Equal(id, revision.GetProperty("articleId").GetGuid());

        var after = await _s.GetArticleAsync(id);
        Assert.Equal("Published", after.GetProperty("status").GetString());
        Assert.Equal(RevisionScenario.VersionOf(before), RevisionScenario.VersionOf(after));
        Assert.Equal("Original title", after.GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_article_shows_the_open_revision_and_stops_offering_revise()
    {
        var id = await _s.PublishedArticleAsync();
        var published = await _s.GetArticleAsync(id);
        Assert.Contains("revise", ActionsOf(published));
        Assert.Equal(JsonValueKind.Null, published.GetProperty("openRevision").ValueKind);
        Assert.Equal(1, published.GetProperty("publishedVersion").GetInt32());

        var revision = await RevisionScenario.ReadAsync(await _s.CreateRevisionAsync(id));

        var article = await _s.GetArticleAsync(id);
        Assert.DoesNotContain("revise", ActionsOf(article));
        var open = article.GetProperty("openRevision");
        Assert.Equal(revision.GetProperty("id").GetGuid(), open.GetProperty("id").GetGuid());
        Assert.Equal("Draft", open.GetProperty("status").GetString());
        Assert.Equal(1, open.GetProperty("number").GetInt32());
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("PendingReview")]
    [InlineData("Rejected")]
    public async Task Only_a_published_article_can_be_revised(string status)
    {
        var created = await _s.Api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", _s.AnnToken,
            new { title = "Not published yet", summary = RevisionScenario.Summary, body = RevisionScenario.Body, categoryId = CategorySeed.Science.Id });
        var article = await RevisionScenario.ReadAsync(created);
        var id = article.GetProperty("id").GetGuid();
        if (status != "Draft")
        {
            var pending = await RevisionScenario.ReadAsync(
                await _s.Api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{id}/submit", _s.AnnToken, new { version = RevisionScenario.VersionOf(article) }));
            if (status == "Rejected")
            {
                var reject = await _s.Api.SendJsonAsync(
                    HttpMethod.Post, $"/api/admin/articles/{id}/reject", _s.AdminToken,
                    new { version = RevisionScenario.VersionOf(pending), submissionVersion = 1, reason = "Needs more detail please." });
                Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
            }
        }

        var response = await _s.CreateRevisionAsync(id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains($"Article cannot be revised in status {status}.", await response.Content.ReadAsStringAsync());
        Assert.Equal(0L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM article_revisions WHERE article_id = @id", ("id", id)));
    }

    [Fact]
    public async Task A_second_revision_cannot_be_started_while_one_is_open()
    {
        var id = await _s.PublishedArticleAsync();
        await _s.CreateRevisionAsync(id);

        var response = await _s.CreateRevisionAsync(id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already has a revision in progress", await response.Content.ReadAsStringAsync());
        Assert.Equal(1L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM article_revisions WHERE article_id = @id", ("id", id)));
    }

    [Fact]
    public async Task Racing_creates_start_exactly_one_revision()
    {
        var id = await _s.PublishedArticleAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _s.CreateRevisionAsync(id)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1L, await _s.Api.ScalarAsync<long>("SELECT count(*) FROM article_revisions WHERE article_id = @id", ("id", id)));
    }

    [Fact]
    public async Task Somebody_elses_article_is_a_404_and_administrators_and_anonymous_callers_are_refused()
    {
        var id = await _s.PublishedArticleAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await _s.CreateRevisionAsync(id, _s.BobToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _s.CreateRevisionAsync(id, _s.AdminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _s.Api.SendAsync(HttpMethod.Post, $"/api/articles/{id}/revision", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.CreateRevisionAsync(Guid.NewGuid())).StatusCode);

        await _s.CreateRevisionAsync(id);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.GetRevisionAsync(id, _s.BobToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.SubmitRevisionAsync(id, 1, _s.BobToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.UpdateRevisionAsync(id, new { title = "Hijack", version = 1 }, _s.BobToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.Api.SendAsync(HttpMethod.Get, $"/api/articles/{id}/revision/reviews", _s.BobToken)).StatusCode);
    }

    [Fact]
    public async Task A_suspended_author_cannot_start_a_revision()
    {
        var id = await _s.PublishedArticleAsync();
        await _s.Api.ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE id = @id", ("id", _s.Ann.Id));

        var response = await _s.CreateRevisionAsync(id);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- get ----

    [Fact]
    public async Task Get_returns_the_open_revision_and_404_when_there_is_none()
    {
        var id = await _s.PublishedArticleAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await _s.GetRevisionAsync(id)).StatusCode);

        var created = await RevisionScenario.ReadAsync(await _s.CreateRevisionAsync(id));

        var response = await _s.GetRevisionAsync(id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.GetProperty("id").GetGuid(), (await RevisionScenario.ReadAsync(response)).GetProperty("id").GetGuid());
    }

    // ---- update ----

    [Fact]
    public async Task Editing_a_revision_changes_only_the_revision()
    {
        var id = await _s.PublishedArticleAsync("Original title");
        var revision = await RevisionScenario.ReadAsync(await _s.CreateRevisionAsync(id));

        var response = await _s.UpdateRevisionAsync(id, new
        {
            title = "  New title  ",
            summary = "New summary",
            body = "New body",
            categoryId = (Guid?)null,
            version = RevisionScenario.VersionOf(revision),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await RevisionScenario.ReadAsync(response);
        Assert.Equal("New title", updated.GetProperty("title").GetString());
        Assert.Equal("New body", updated.GetProperty("body").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("category").ValueKind);
        Assert.NotEqual(RevisionScenario.VersionOf(revision), RevisionScenario.VersionOf(updated));

        var article = await _s.GetArticleAsync(id);
        Assert.Equal("Original title", article.GetProperty("title").GetString());
        Assert.Equal(RevisionScenario.Body, article.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Editing_applies_the_draft_limits_and_checks_the_category()
    {
        var id = await _s.PublishedArticleAsync();
        var revision = await RevisionScenario.ReadAsync(await _s.CreateRevisionAsync(id));
        var version = RevisionScenario.VersionOf(revision);

        var response = await _s.UpdateRevisionAsync(id, new
        {
            title = " ",
            summary = new string('s', 501),
            body = new string('b', 20001),
            categoryId = Guid.NewGuid(),
            version,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("summary", out _));
        Assert.True(errors.TryGetProperty("body", out _));
        Assert.True(errors.TryGetProperty("categoryId", out _));
        Assert.Equal(version, RevisionScenario.VersionOf(await RevisionScenario.ReadAsync(await _s.GetRevisionAsync(id))));
    }

    [Fact]
    public async Task Editing_needs_a_version_and_a_stale_one_is_a_409_with_the_current_version()
    {
        var id = await _s.PublishedArticleAsync();
        var revision = await RevisionScenario.ReadAsync(await _s.CreateRevisionAsync(id));
        var version = RevisionScenario.VersionOf(revision);

        var missing = await _s.UpdateRevisionAsync(id, new { title = "No version" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var first = await _s.UpdateRevisionAsync(id, new { title = "First edit", version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var current = RevisionScenario.VersionOf(await RevisionScenario.ReadAsync(first));

        var stale = await _s.UpdateRevisionAsync(id, new { title = "Second edit", version });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await RevisionScenario.ReadAsync(stale);
        Assert.Equal(current, problem.GetProperty("currentVersion").GetUInt32());
        Assert.Equal("First edit", (await RevisionScenario.ReadAsync(await _s.GetRevisionAsync(id))).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_pending_revision_is_read_only()
    {
        var id = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(id);

        var response = await _s.UpdateRevisionAsync(id, new { title = "Too late", version = RevisionScenario.VersionOf(pending) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Revision cannot be modified in status PendingReview.", await response.Content.ReadAsStringAsync());
    }

    // ---- submit / withdraw ----

    [Fact]
    public async Task Submitting_applies_the_full_submission_rules_and_changes_nothing_when_they_fail()
    {
        var id = await _s.PublishedArticleAsync();
        var revision = await RevisionScenario.ReadAsync(await _s.CreateRevisionAsync(id));
        var edited = await RevisionScenario.ReadAsync(await _s.UpdateRevisionAsync(id, new
        {
            title = "abc",
            summary = "short",
            body = "short",
            categoryId = (Guid?)null,
            version = RevisionScenario.VersionOf(revision),
        }));

        var response = await _s.SubmitRevisionAsync(id, RevisionScenario.VersionOf(edited));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Equal(["body", "categoryId", "summary", "title"], errors.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var current = await RevisionScenario.ReadAsync(await _s.GetRevisionAsync(id));
        Assert.Equal("Draft", current.GetProperty("status").GetString());
        Assert.Equal(0, current.GetProperty("submissionVersion").GetInt32());
    }

    [Fact]
    public async Task Submitting_sends_the_revision_to_review()
    {
        var id = await _s.PublishedArticleAsync();
        var draft = await _s.DraftRevisionAsync(id);

        var response = await _s.SubmitRevisionAsync(id, RevisionScenario.VersionOf(draft));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pending = await RevisionScenario.ReadAsync(response);
        Assert.Equal("PendingReview", pending.GetProperty("status").GetString());
        Assert.Equal(1, pending.GetProperty("submissionVersion").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, pending.GetProperty("submittedAt").ValueKind);
        Assert.Equal(["withdraw"], ActionsOf(pending));

        // The article readers see is unchanged.
        var article = await _s.GetArticleAsync(id);
        Assert.Equal("Published", article.GetProperty("status").GetString());
        Assert.Equal("A published article", article.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Submitting_twice_is_a_409()
    {
        var id = await _s.PublishedArticleAsync();
        var draft = await _s.DraftRevisionAsync(id);
        var version = RevisionScenario.VersionOf(draft);
        await _s.SubmitRevisionAsync(id, version);

        var again = await _s.SubmitRevisionAsync(id, version);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Revision cannot be submitted in status PendingReview.", await again.Content.ReadAsStringAsync());

        Assert.Equal(1, await _s.Api.ScalarAsync<int>("SELECT submission_version FROM article_revisions WHERE article_id = @id", ("id", id)));
    }

    [Fact]
    public async Task Submitting_with_a_stale_version_reports_the_current_one()
    {
        var id = await _s.PublishedArticleAsync();
        var draft = await _s.DraftRevisionAsync(id);
        var current = RevisionScenario.VersionOf(draft);

        var response = await _s.SubmitRevisionAsync(id, current + 100);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(current, (await RevisionScenario.ReadAsync(response)).GetProperty("currentVersion").GetUInt32());
    }

    [Fact]
    public async Task Withdrawing_returns_the_revision_to_draft_and_it_can_be_edited_and_submitted_again()
    {
        var id = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(id);

        var response = await _s.WithdrawRevisionAsync(id, RevisionScenario.VersionOf(pending));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await RevisionScenario.ReadAsync(response);
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal(["edit", "submit"], ActionsOf(draft));

        var resubmitted = await _s.SubmitRevisionAsync(id, RevisionScenario.VersionOf(draft));
        Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
        Assert.Equal(2, (await RevisionScenario.ReadAsync(resubmitted)).GetProperty("submissionVersion").GetInt32());
    }

    [Fact]
    public async Task Only_a_pending_revision_can_be_withdrawn()
    {
        var id = await _s.PublishedArticleAsync();
        var draft = await _s.DraftRevisionAsync(id);

        var response = await _s.WithdrawRevisionAsync(id, RevisionScenario.VersionOf(draft));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Revision cannot be withdrawn in status Draft.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Acting_on_an_article_without_a_revision_is_a_404()
    {
        var id = await _s.PublishedArticleAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await _s.SubmitRevisionAsync(id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.WithdrawRevisionAsync(id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _s.UpdateRevisionAsync(id, new { title = "Nothing", version = 1 })).StatusCode);
    }

    [Fact]
    public async Task The_review_list_is_empty_until_an_administrator_decides()
    {
        var id = await _s.PublishedArticleAsync();
        await _s.PendingRevisionAsync(id);

        var response = await _s.Api.SendAsync(HttpMethod.Get, $"/api/articles/{id}/revision/reviews", _s.AnnToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await RevisionScenario.ReadAsync(response)).EnumerateArray());
    }
}
