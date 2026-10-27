using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Auditing;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Reviews;

[Collection(PostgresCollection.Name)]
public class ReviewDecisionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string Summary = new('s', 30);
    private static readonly string Body = new('b', 150);
    private const string Reason = "Please add concrete examples and sources.";

    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private Domain.Entities.User _ann = null!;
    private string _adminToken = null!;
    private string _annToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _ann = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _annToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    // ---- helpers ----

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Submitted(Guid Id, uint Version, int SubmissionVersion);

    /// <summary>The author writes a complete article and submits it, exactly as in the portal.</summary>
    private async Task<Submitted> SubmitAsync(string title = "An article for review")
    {
        var created = await ReadAsync(await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", _annToken,
            new { title, summary = Summary, body = Body, categoryId = CategorySeed.Science.Id }));
        var id = created.GetProperty("id").GetGuid();
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{id}/submit", _annToken, new { version = created.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var article = await ReadAsync(response);
        return new Submitted(id, article.GetProperty("version").GetUInt32(), article.GetProperty("submissionVersion").GetInt32());
    }

    private Task<HttpResponseMessage> ApproveAsync(Submitted a, string? token = null, uint? version = null, int? submissionVersion = null) =>
        _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/articles/{a.Id}/approve", token ?? _adminToken,
            new { version = version ?? a.Version, submissionVersion = submissionVersion ?? a.SubmissionVersion });

    private Task<HttpResponseMessage> RejectAsync(Submitted a, string? reason = Reason, string? token = null) =>
        _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/articles/{a.Id}/reject", token ?? _adminToken,
            new { version = a.Version, submissionVersion = a.SubmissionVersion, reason });

    private Task<string> ArticleColumnAsync(Guid id, string column) =>
        _api.ScalarAsync<string>($"SELECT coalesce({column}::text, '<null>') FROM articles WHERE id = @id", ("id", id));

    private Task<long> ReviewCountAsync(Guid id) =>
        _api.ScalarAsync<long>("SELECT count(*) FROM reviews WHERE article_id = @id", ("id", id));

    private async Task<JsonElement> AuthorViewAsync(Guid id) =>
        await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}", _annToken));

    // ---- approve ----

    [Fact]
    public async Task Approving_publishes_the_article_and_stores_the_review()
    {
        var a = await SubmitAsync();
        _api.Clock.Advance(TimeSpan.FromHours(1));

        var response = await ApproveAsync(a);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadAsync(response);
        Assert.Equal("Published", detail.GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canReview").GetBoolean());
        Assert.Equal(_api.Clock.GetUtcNow(), detail.GetProperty("publishedAt").GetDateTimeOffset());
        var entry = Assert.Single(detail.GetProperty("reviewHistory").EnumerateArray());
        Assert.Equal("Approved", entry.GetProperty("decision").GetString());
        Assert.Equal(_admin.Id, entry.GetProperty("reviewer").GetProperty("id").GetGuid());

        Assert.Equal("Published", await ArticleColumnAsync(a.Id, "status"));
        Assert.Equal(1, await ReviewCountAsync(a.Id));
        Assert.Equal("Approved", await _api.ScalarAsync<string>("SELECT decision FROM reviews WHERE article_id = @id", ("id", a.Id)));
        Assert.Equal(1, await _api.ScalarAsync<int>("SELECT submission_version FROM reviews WHERE article_id = @id", ("id", a.Id)));
        Assert.Equal(_admin.Id.ToString(), await _api.ScalarAsync<string>("SELECT reviewer_id::text FROM reviews WHERE article_id = @id", ("id", a.Id)));
        Assert.Equal("<null>", await _api.ScalarAsync<string>("SELECT coalesce(reason, '<null>') FROM reviews WHERE article_id = @id", ("id", a.Id)));
    }

    [Fact]
    public async Task The_author_sees_a_published_article_that_can_only_be_revised_and_the_approval()
    {
        var a = await SubmitAsync();
        await ApproveAsync(a);

        var article = await AuthorViewAsync(a.Id);

        Assert.Equal("Published", article.GetProperty("status").GetString());
        Assert.Equal(["revise"], article.GetProperty("allowedActions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("Approved", article.GetProperty("latestReview").GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Approving_writes_one_audit_event_by_the_reviewer()
    {
        var a = await SubmitAsync();
        var before = _api.Audit.Events.Count;

        await ApproveAsync(a);

        var audit = Assert.Single(_api.Audit.Events.Skip(before));
        Assert.Equal(AuditActions.ArticleApproved, audit.Action);
        Assert.Equal("Article", audit.EntityType);
        Assert.Equal(a.Id, audit.EntityId);
        Assert.Equal(_admin.Id, audit.ActorUserId);
        var details = JsonDocument.Parse(audit.DetailsJson).RootElement;
        Assert.Equal(a.Id, details.GetProperty("articleId").GetGuid());
        Assert.Equal(1, details.GetProperty("submissionVersion").GetInt32());
    }

    // ---- reject ----

    [Fact]
    public async Task Rejecting_stores_the_trimmed_reason_and_the_author_sees_it()
    {
        var a = await SubmitAsync();

        var response = await RejectAsync(a, $"   {Reason}   ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rejected", (await ReadAsync(response)).GetProperty("status").GetString());
        Assert.Equal("Rejected", await ArticleColumnAsync(a.Id, "status"));
        Assert.Equal("<null>", await ArticleColumnAsync(a.Id, "published_at"));
        Assert.Equal(Reason, await _api.ScalarAsync<string>("SELECT reason FROM reviews WHERE article_id = @id", ("id", a.Id)));

        var article = await AuthorViewAsync(a.Id);
        Assert.Equal("Rejected", article.GetProperty("latestReview").GetProperty("decision").GetString());
        Assert.Equal(Reason, article.GetProperty("latestReview").GetProperty("reason").GetString());
        Assert.Equal(["edit", "submit"], article.GetProperty("allowedActions").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Rejecting_writes_an_audit_event_with_the_reason()
    {
        var a = await SubmitAsync();
        var before = _api.Audit.Events.Count;

        await RejectAsync(a);

        var audit = Assert.Single(_api.Audit.Events.Skip(before));
        Assert.Equal(AuditActions.ArticleRejected, audit.Action);
        Assert.Equal(_admin.Id, audit.ActorUserId);
        Assert.Equal(Reason, JsonDocument.Parse(audit.DetailsJson).RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    [InlineData("too short")]
    public async Task A_missing_or_short_reason_is_a_400_and_nothing_changes(string? reason)
    {
        var a = await SubmitAsync();
        var before = _api.Audit.Events.Count;

        var response = await RejectAsync(a, reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("reason", out _));
        Assert.Equal("PendingReview", await ArticleColumnAsync(a.Id, "status"));
        Assert.Equal(0, await ReviewCountAsync(a.Id));
        Assert.Equal(before, _api.Audit.Events.Count);
    }

    [Theory]
    [InlineData(10, HttpStatusCode.OK)]
    [InlineData(1000, HttpStatusCode.OK)]
    [InlineData(1001, HttpStatusCode.BadRequest)]
    public async Task The_reason_limits_are_inclusive(int length, HttpStatusCode expected)
    {
        var a = await SubmitAsync();

        var response = await RejectAsync(a, new string('r', length));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_conflict_is_reported_before_a_bad_reason()
    {
        var a = await SubmitAsync();
        await ApproveAsync(a);

        var response = await RejectAsync(a, "short");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Version_and_submission_version_are_required()
    {
        var a = await SubmitAsync();

        var none = await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/articles/{a.Id}/approve", _adminToken, new { });
        var zero = await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/articles/{a.Id}/approve", _adminToken, new { version = a.Version, submissionVersion = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal("PendingReview", await ArticleColumnAsync(a.Id, "status"));
    }

    // ---- conflicts ----

    [Fact]
    public async Task An_unknown_article_and_a_private_draft_are_404()
    {
        var created = await ReadAsync(await _api.SendJsonAsync(HttpMethod.Post, "/api/articles", _annToken, new { title = "Private draft" }));
        var draft = new Submitted(created.GetProperty("id").GetGuid(), created.GetProperty("version").GetUInt32(), 1);

        Assert.Equal(HttpStatusCode.NotFound, (await ApproveAsync(draft)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ApproveAsync(new Submitted(Guid.NewGuid(), 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RejectAsync(new Submitted(Guid.NewGuid(), 1, 1))).StatusCode);
    }

    [Fact]
    public async Task A_suspended_authors_article_cannot_be_decided()
    {
        var a = await SubmitAsync();
        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE id = @id", ("id", _ann.Id));

        var approve = await ApproveAsync(a);
        var reject = await RejectAsync(a);

        foreach (var response in new[] { approve, reject })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await ReadAsync(response);
            Assert.Equal("Review is blocked because the author account is suspended.", problem.GetProperty("detail").GetString());
            Assert.Equal("PendingReview", problem.GetProperty("currentStatus").GetString());
        }

        Assert.Equal("PendingReview", await ArticleColumnAsync(a.Id, "status"));
        Assert.Equal(0, await ReviewCountAsync(a.Id));
    }

    [Fact]
    public async Task An_article_that_was_already_decided_is_a_409_with_its_current_status()
    {
        var a = await SubmitAsync();
        await ApproveAsync(a);

        var again = await ApproveAsync(a);
        var other = await RejectAsync(a);

        foreach (var response in new[] { again, other })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await ReadAsync(response);
            Assert.Equal("Published", problem.GetProperty("currentStatus").GetString());
            Assert.True(problem.TryGetProperty("currentVersion", out _));
            Assert.Contains("no longer pending review", problem.GetProperty("detail").GetString());
        }

        Assert.Equal(1, await ReviewCountAsync(a.Id));
    }

    [Fact]
    public async Task A_stale_version_is_a_409_that_names_the_current_one()
    {
        var a = await SubmitAsync();

        var response = await ApproveAsync(a, version: a.Version + 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadAsync(response);
        Assert.Equal(a.Version, problem.GetProperty("currentVersion").GetUInt32());
        Assert.Equal("PendingReview", problem.GetProperty("currentStatus").GetString());
        Assert.Equal("PendingReview", await ArticleColumnAsync(a.Id, "status"));
    }

    [Fact]
    public async Task A_decision_on_an_older_submission_is_refused_and_the_newer_one_can_be_decided()
    {
        var a = await SubmitAsync();
        await RejectAsync(a);
        var author = await AuthorViewAsync(a.Id);
        var resubmitted = await ReadAsync(await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{a.Id}/submit", _annToken, new { version = author.GetProperty("version").GetUInt32() }));
        var second = new Submitted(a.Id, resubmitted.GetProperty("version").GetUInt32(), 2);

        var old = await ApproveAsync(second, submissionVersion: 1);
        var current = await ApproveAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, old.StatusCode);
        Assert.Contains("newer version", (await ReadAsync(old)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(2, await ReviewCountAsync(a.Id));
        var decisions = (await ReadAsync(current)).GetProperty("reviewHistory").EnumerateArray()
            .Select(h => h.GetProperty("decision").GetString()!).ToList();
        Assert.Equal(["Approved", "Rejected"], decisions);
    }

    // ---- races ----

    [Fact]
    public async Task Simultaneous_approvals_let_exactly_one_succeed_and_store_one_review()
    {
        for (var round = 0; round < 6; round++)
        {
            var a = await SubmitAsync($"Approval race {round}");

            var responses = await Task.WhenAll(ApproveAsync(a), ApproveAsync(a), ApproveAsync(a));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await ReviewCountAsync(a.Id));
            Assert.Equal("Published", await ArticleColumnAsync(a.Id, "status"));
        }
    }

    [Fact]
    public async Task An_approval_and_a_rejection_at_the_same_moment_let_exactly_one_win()
    {
        for (var round = 0; round < 6; round++)
        {
            var a = await SubmitAsync($"Mixed race {round}");

            var responses = await Task.WhenAll(ApproveAsync(a), RejectAsync(a));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await ReviewCountAsync(a.Id));
        }
    }

    [Fact]
    public async Task A_decision_racing_the_authors_withdrawal_lets_exactly_one_succeed()
    {
        for (var round = 0; round < 10; round++)
        {
            var a = await SubmitAsync($"Withdraw race {round}");

            var approve = ApproveAsync(a);
            var withdraw = _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{a.Id}/withdraw", _annToken, new { version = a.Version });
            var responses = await Task.WhenAll(approve, withdraw);

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            if (responses[0].StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal("Published", await ArticleColumnAsync(a.Id, "status"));
                Assert.Equal(1, await ReviewCountAsync(a.Id));
            }
            else
            {
                Assert.Equal("Draft", await ArticleColumnAsync(a.Id, "status"));
                Assert.Equal(0, await ReviewCountAsync(a.Id));
            }
        }
    }

    [Fact]
    public async Task The_unique_review_per_submission_is_the_last_line_of_defence()
    {
        var a = await SubmitAsync();
        await _api.ExecuteAsync(
            "INSERT INTO reviews (id, article_id, submission_version, reviewer_id, decision, reason, decided_at) " +
            "VALUES (gen_random_uuid(), @a, 1, @r, 'Approved', NULL, now())", ("a", a.Id), ("r", _admin.Id));

        var response = await ApproveAsync(a);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadAsync(response);
        Assert.Contains("has changed since you opened it", problem.GetProperty("detail").GetString());
        Assert.Equal("PendingReview", problem.GetProperty("currentStatus").GetString());
        Assert.Equal(1, await ReviewCountAsync(a.Id));
        Assert.Equal("PendingReview", await ArticleColumnAsync(a.Id, "status"));
    }

    // ---- who may call, and what is not possible ----

    [Fact]
    public async Task Authors_get_403_and_visitors_401()
    {
        var a = await SubmitAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(a, _annToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RejectAsync(a, token: _annToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/articles/{a.Id}/approve", null, new { version = a.Version, submissionVersion = 1 })).StatusCode);
        Assert.Equal("PendingReview", await ArticleColumnAsync(a.Id, "status"));
    }

    [Fact]
    public async Task No_admin_endpoint_edits_an_articles_content()
    {
        var a = await SubmitAsync("Untouchable");
        var body = new { title = "Hacked", summary = "Hacked summary text here.", body = "Hacked", version = a.Version };

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Post, HttpMethod.Delete })
        {
            var response = await _api.SendJsonAsync(method, $"/api/admin/articles/{a.Id}", _adminToken, body);
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{method}: {(int)response.StatusCode}");
        }

        Assert.Equal("Untouchable", await ArticleColumnAsync(a.Id, "title"));
        Assert.Equal(Summary, await ArticleColumnAsync(a.Id, "summary"));
    }

    [Fact]
    public async Task A_failure_while_recording_the_audit_event_leaves_the_article_pending()
    {
        var a = await SubmitAsync();
        _api.Audit.FailOn = AuditActions.ArticleApproved;

        var response = await ApproveAsync(a);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("PendingReview", await ArticleColumnAsync(a.Id, "status"));
        Assert.Equal(0, await ReviewCountAsync(a.Id));
    }
}
