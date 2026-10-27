using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Auditing;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Articles;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ContentReview.IntegrationTests.Articles;

[Collection(PostgresCollection.Name)]
public class ArticleSubmissionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string RaceMessage = "The article has changed. Refresh to see its current status.";

    private static readonly string Summary = new('s', 30);
    private static readonly string Body = new('b', 150);

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

    private static uint VersionOf(JsonElement article) => article.GetProperty("version").GetUInt32();

    private static Guid IdOf(JsonElement article) => article.GetProperty("id").GetGuid();

    private Task<HttpResponseMessage> SubmitAsync(Guid id, object body, string? token = null) =>
        _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{id}/submit", token ?? _annToken, body);

    private Task<HttpResponseMessage> WithdrawAsync(Guid id, object body, string? token = null) =>
        _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{id}/withdraw", token ?? _annToken, body);

    private async Task<JsonElement> GetAsync(Guid id)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"/api/articles/{id}", _annToken);
        Assert.True(response.IsSuccessStatusCode, $"GET article {id}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await ReadAsync(response);
    }

    private async Task<uint> CurrentVersionAsync(Guid id) => VersionOf(await GetAsync(id));

    /// <summary>A complete draft that meets every submission rule.</summary>
    private async Task<JsonElement> CreateReadyDraftAsync(string title = "A ready article")
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", _annToken, new { title, summary = Summary, body = Body, categoryId = CategorySeed.Science.Id });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private Task<string> ColumnAsync(Guid id, string column) =>
        _api.ScalarAsync<string>($"SELECT coalesce({column}::text, '<null>') FROM articles WHERE id = @id", ("id", id));

    private Task<long> SubmissionCountAsync(Guid id) =>
        _api.ScalarAsync<long>("SELECT count(*) FROM article_submissions WHERE article_id = @id", ("id", id));

    private Task<string> SubmissionColumnAsync(Guid id, int version, string column) =>
        _api.ScalarAsync<string>(
            $"SELECT coalesce({column}::text, '<null>') FROM article_submissions WHERE article_id = @id AND version = @v",
            ("id", id), ("v", version));

    private async Task<Guid> InsertArticleAsync(string status, int submissionVersion = 0, string? title = null, string? summary = null, string? body = null)
    {
        var id = Guid.CreateVersion7();
        await _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, category_id, title, summary, body, status, submission_version, created_at, updated_at) " +
            "VALUES (@id, @author, @c, @title, @summary, @body, @status, @sv, now(), now())",
            ("id", id), ("author", _ann.Id), ("c", CategorySeed.Science.Id), ("title", title ?? "Seeded article"),
            ("summary", summary ?? Summary), ("body", body ?? Body), ("status", status), ("sv", submissionVersion));
        return id;
    }

    private async Task AddSubmissionAsync(Guid articleId, int version, string title = "Old title")
    {
        await _api.ExecuteAsync(
            "INSERT INTO article_submissions (id, article_id, version, title, summary, body, category_id, submitted_at) " +
            "VALUES (@id, @a, @v, @t, @s, @b, @c, now() - interval '1 day')",
            ("id", Guid.CreateVersion7()), ("a", articleId), ("v", version), ("t", title), ("s", Summary), ("b", Body), ("c", CategorySeed.Science.Id));
    }

    private async Task AddRejectionAsync(Guid articleId, int version, string reason)
    {
        await _api.ExecuteAsync(
            "INSERT INTO reviews (id, article_id, submission_version, reviewer_id, decision, reason, decided_at) " +
            "VALUES (@id, @a, @v, @r, 'Rejected', @reason, now() - interval '12 hours')",
            ("id", Guid.CreateVersion7()), ("a", articleId), ("v", version), ("r", _admin.Id), ("reason", reason));
    }

    // ---- submit ----

    [Fact]
    public async Task Submitting_a_valid_draft_makes_it_pending_review_and_stores_a_snapshot()
    {
        var draft = await CreateReadyDraftAsync("Snapshot me");
        _api.Clock.Advance(TimeSpan.FromHours(1));

        var response = await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var article = await ReadAsync(response);
        Assert.Equal("PendingReview", article.GetProperty("status").GetString());
        Assert.Equal(1, article.GetProperty("submissionVersion").GetInt32());
        Assert.Equal(_api.Clock.GetUtcNow(), article.GetProperty("submittedAt").GetDateTimeOffset());
        Assert.Equal(["withdraw"], article.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        Assert.NotEqual(VersionOf(draft), VersionOf(article));

        var id = IdOf(draft);
        Assert.Equal(1, await SubmissionCountAsync(id));
        Assert.Equal("Snapshot me", await SubmissionColumnAsync(id, 1, "title"));
        Assert.Equal(Summary, await SubmissionColumnAsync(id, 1, "summary"));
        Assert.Equal(Body, await SubmissionColumnAsync(id, 1, "body"));
        Assert.Equal(CategorySeed.Science.Id.ToString(), await SubmissionColumnAsync(id, 1, "category_id"));
        Assert.Equal("<null>", await SubmissionColumnAsync(id, 1, "withdrawn_at"));
    }

    [Fact]
    public async Task Submitting_writes_one_audit_event_with_the_article_and_the_submission_version()
    {
        var draft = await CreateReadyDraftAsync();

        await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) });

        var audit = Assert.Single(_api.Audit.Events);
        Assert.Equal(AuditActions.ArticleSubmitted, audit.Action);
        Assert.Equal("Article", audit.EntityType);
        Assert.Equal(IdOf(draft), audit.EntityId);
        Assert.Equal(_ann.Id, audit.ActorUserId);
        var details = JsonDocument.Parse(audit.DetailsJson).RootElement;
        Assert.Equal(IdOf(draft), details.GetProperty("articleId").GetGuid());
        Assert.Equal(1, details.GetProperty("submissionVersion").GetInt32());
        Assert.DoesNotContain(Body, audit.DetailsJson);
    }

    [Fact]
    public async Task An_incomplete_draft_gets_a_400_with_every_broken_field_and_nothing_changes()
    {
        var created = await ReadAsync(await _api.SendJsonAsync(HttpMethod.Post, "/api/articles", _annToken, new { title = "Hey" }));

        var response = await SubmitAsync(IdOf(created), new { version = VersionOf(created) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.Equal("Title must be between 5 and 150 characters.", errors.GetProperty("title")[0].GetString());
        Assert.Equal("Summary is required.", errors.GetProperty("summary")[0].GetString());
        Assert.Equal("Body is required.", errors.GetProperty("body")[0].GetString());
        Assert.Equal("Category is required.", errors.GetProperty("categoryId")[0].GetString());
        Assert.Equal("Draft", await ColumnAsync(IdOf(created), "status"));
        Assert.Equal("0", await ColumnAsync(IdOf(created), "submission_version"));
        Assert.Equal(0, await SubmissionCountAsync(IdOf(created)));
        Assert.Equal(VersionOf(created), await CurrentVersionAsync(IdOf(created)));
        Assert.Empty(_api.Audit.Events);
    }

    [Theory]
    [InlineData(5, 20, 100, HttpStatusCode.OK)]
    [InlineData(4, 20, 100, HttpStatusCode.BadRequest)]
    [InlineData(5, 19, 100, HttpStatusCode.BadRequest)]
    [InlineData(5, 20, 99, HttpStatusCode.BadRequest)]
    [InlineData(150, 500, 20000, HttpStatusCode.OK)]
    public async Task The_submission_limits_are_inclusive(int title, int summary, int body, HttpStatusCode expected)
    {
        var created = await ReadAsync(await _api.SendJsonAsync(
            HttpMethod.Post,
            "/api/articles",
            _annToken,
            new { title = new string('t', title), summary = new string('s', summary), body = new string('b', body), categoryId = CategorySeed.Health.Id }));

        var response = await SubmitAsync(IdOf(created), new { version = VersionOf(created) });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Padding_with_spaces_does_not_meet_a_minimum()
    {
        var created = await ReadAsync(await _api.SendJsonAsync(
            HttpMethod.Post,
            "/api/articles",
            _annToken,
            new { title = "ab    ", summary = "short" + new string(' ', 40), body = "tiny" + new string(' ', 200), categoryId = CategorySeed.Health.Id }));

        var response = await SubmitAsync(IdOf(created), new { version = VersionOf(created) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_version_is_a_400()
    {
        var draft = await CreateReadyDraftAsync();

        var response = await SubmitAsync(IdOf(draft), new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("version", out _));
        Assert.Equal("Draft", await ColumnAsync(IdOf(draft), "status"));
    }

    [Fact]
    public async Task A_stale_version_is_a_409_with_the_current_version_and_nothing_changes()
    {
        var draft = await CreateReadyDraftAsync();
        var stale = VersionOf(draft);
        var edited = await ReadAsync(await _api.SendJsonAsync(
            HttpMethod.Put, $"/api/articles/{IdOf(draft)}", _annToken,
            new { title = "Edited meanwhile", summary = Summary, body = Body, categoryId = CategorySeed.Science.Id, version = stale }));

        var response = await SubmitAsync(IdOf(draft), new { version = stale });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(VersionOf(edited), (await ReadAsync(response)).GetProperty("currentVersion").GetUInt32());
        Assert.Equal("Draft", await ColumnAsync(IdOf(draft), "status"));
        Assert.Equal(0, await SubmissionCountAsync(IdOf(draft)));
    }

    [Theory]
    [InlineData("PendingReview")]
    [InlineData("Published")]
    public async Task Only_a_draft_or_a_rejected_article_can_be_submitted(string status)
    {
        var id = await InsertArticleAsync(status, submissionVersion: 1);
        await AddSubmissionAsync(id, 1);

        var response = await SubmitAsync(id, new { version = await CurrentVersionAsync(id) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"Article cannot be submitted in status {status}.", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Equal(1, await SubmissionCountAsync(id));
        Assert.Empty(_api.Audit.Events);
    }

    [Fact]
    public async Task Repeating_a_submit_with_the_same_version_changes_nothing_the_second_time()
    {
        var draft = await CreateReadyDraftAsync();
        var version = VersionOf(draft);

        var first = await SubmitAsync(IdOf(draft), new { version });
        var second = await SubmitAsync(IdOf(draft), new { version });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await SubmissionCountAsync(IdOf(draft)));
        Assert.Equal("1", await ColumnAsync(IdOf(draft), "submission_version"));
        Assert.Single(_api.Audit.Events);
    }

    [Fact]
    public async Task Simultaneous_submits_with_one_version_let_exactly_one_win_and_store_one_snapshot()
    {
        for (var round = 0; round < 8; round++)
        {
            var draft = await CreateReadyDraftAsync($"Race round {round}");
            var version = VersionOf(draft);

            var responses = await Task.WhenAll(
                SubmitAsync(IdOf(draft), new { version }),
                SubmitAsync(IdOf(draft), new { version }),
                SubmitAsync(IdOf(draft), new { version }));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await SubmissionCountAsync(IdOf(draft)));
            Assert.Equal("1", await ColumnAsync(IdOf(draft), "submission_version"));
            Assert.Equal("PendingReview", await ColumnAsync(IdOf(draft), "status"));
        }

        // (The audit writer is not asserted here: the test double records when asked, the real one (F13.1) joins the transaction.)
    }

    [Fact]
    public async Task A_snapshot_that_already_exists_for_the_next_version_is_a_409_not_a_500()
    {
        // The unique (article, version) index is the last line of defence against a duplicate submission row.
        var draft = await CreateReadyDraftAsync();
        await AddSubmissionAsync(IdOf(draft), 1);

        var response = await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RaceMessage, (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Equal("Draft", await ColumnAsync(IdOf(draft), "status"));
        Assert.Equal("0", await ColumnAsync(IdOf(draft), "submission_version"));
        Assert.Equal(1, await SubmissionCountAsync(IdOf(draft)));
    }

    [Fact]
    public async Task A_rejected_article_is_resubmitted_as_version_2_and_the_first_review_stays()
    {
        var id = await InsertArticleAsync("Rejected", submissionVersion: 1, title: "Fixed after feedback");
        await AddSubmissionAsync(id, 1, title: "Original title");
        await AddRejectionAsync(id, 1, "Please add sources and examples.");
        _api.Clock.Advance(TimeSpan.FromHours(1));

        var response = await SubmitAsync(id, new { version = await CurrentVersionAsync(id) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var article = await ReadAsync(response);
        Assert.Equal("PendingReview", article.GetProperty("status").GetString());
        Assert.Equal(2, article.GetProperty("submissionVersion").GetInt32());
        Assert.Equal(2, await SubmissionCountAsync(id));
        Assert.Equal("Original title", await SubmissionColumnAsync(id, 1, "title"));
        Assert.Equal("Fixed after feedback", await SubmissionColumnAsync(id, 2, "title"));
        Assert.Equal(1, await _api.ScalarAsync<long>("SELECT count(*) FROM reviews WHERE article_id = @id", ("id", id)));
        Assert.Equal("Please add sources and examples.", await _api.ScalarAsync<string>("SELECT reason FROM reviews WHERE article_id = @id", ("id", id)));
        Assert.Equal(2, JsonDocument.Parse(Assert.Single(_api.Audit.Events).DetailsJson).RootElement.GetProperty("submissionVersion").GetInt32());
    }

    [Fact]
    public async Task A_resubmission_is_validated_again()
    {
        var id = await InsertArticleAsync("Rejected", submissionVersion: 1, summary: "too short");
        await AddSubmissionAsync(id, 1);

        var response = await SubmitAsync(id, new { version = await CurrentVersionAsync(id) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("summary", out _));
        Assert.Equal("Rejected", await ColumnAsync(id, "status"));
        Assert.Equal(1, await SubmissionCountAsync(id));
    }

    [Fact]
    public async Task Editing_a_rejected_article_never_changes_the_stored_submission()
    {
        var id = await InsertArticleAsync("Rejected", submissionVersion: 1, title: "Rejected title");
        await AddSubmissionAsync(id, 1, title: "Rejected title");
        await AddRejectionAsync(id, 1, "Not good enough yet, sorry.");

        var edited = await _api.SendJsonAsync(
            HttpMethod.Put, $"/api/articles/{id}", _annToken,
            new { title = "Edited title", summary = Summary, body = Body, categoryId = CategorySeed.Science.Id, version = await CurrentVersionAsync(id) });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("Rejected title", await SubmissionColumnAsync(id, 1, "title"));
    }

    // ---- withdraw ----

    [Fact]
    public async Task Withdrawing_returns_the_article_to_draft_and_marks_the_submission_withdrawn()
    {
        var draft = await CreateReadyDraftAsync();
        var submitted = await ReadAsync(await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) }));
        _api.Clock.Advance(TimeSpan.FromMinutes(10));

        var response = await WithdrawAsync(IdOf(draft), new { version = VersionOf(submitted) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var article = await ReadAsync(response);
        Assert.Equal("Draft", article.GetProperty("status").GetString());
        Assert.Equal(1, article.GetProperty("submissionVersion").GetInt32());
        Assert.Equal(["edit", "delete", "submit"], article.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(
            _api.Clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm"),
            DateTime.Parse(await SubmissionColumnAsync(IdOf(draft), 1, "withdrawn_at")).ToUniversalTime().ToString("yyyy-MM-dd HH:mm"));
        Assert.Equal(1, await SubmissionCountAsync(IdOf(draft)));
        var audit = _api.Audit.Events.Last();
        Assert.Equal(AuditActions.ArticleWithdrawn, audit.Action);
        Assert.Equal(_ann.Id, audit.ActorUserId);
        var details = JsonDocument.Parse(audit.DetailsJson).RootElement;
        Assert.Equal(IdOf(draft), details.GetProperty("articleId").GetGuid());
        Assert.Equal(1, details.GetProperty("submissionVersion").GetInt32());
    }

    [Fact]
    public async Task A_withdrawn_article_can_be_edited_and_submitted_again_as_the_next_version()
    {
        var draft = await CreateReadyDraftAsync();
        var submitted = await ReadAsync(await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) }));
        var back = await ReadAsync(await WithdrawAsync(IdOf(draft), new { version = VersionOf(submitted) }));

        var again = await SubmitAsync(IdOf(draft), new { version = VersionOf(back) });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await ReadAsync(again)).GetProperty("submissionVersion").GetInt32());
        Assert.Equal(2, await SubmissionCountAsync(IdOf(draft)));
        Assert.NotEqual("<null>", await SubmissionColumnAsync(IdOf(draft), 1, "withdrawn_at"));
        Assert.Equal("<null>", await SubmissionColumnAsync(IdOf(draft), 2, "withdrawn_at"));
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Rejected")]
    [InlineData("Published")]
    public async Task Only_a_pending_article_can_be_withdrawn(string status)
    {
        var id = await InsertArticleAsync(status, submissionVersion: status == "Draft" ? 0 : 1);

        var response = await WithdrawAsync(id, new { version = await CurrentVersionAsync(id) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"Article cannot be withdrawn in status {status}.", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Equal(status, await ColumnAsync(id, "status"));
        Assert.Empty(_api.Audit.Events);
    }

    [Fact]
    public async Task A_stale_version_on_withdraw_is_a_409_and_the_article_stays_pending()
    {
        var draft = await CreateReadyDraftAsync();
        var submitted = await ReadAsync(await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) }));

        var response = await WithdrawAsync(IdOf(draft), new { version = VersionOf(submitted) + 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(VersionOf(submitted), (await ReadAsync(response)).GetProperty("currentVersion").GetUInt32());
        Assert.Equal("PendingReview", await ColumnAsync(IdOf(draft), "status"));
    }

    [Fact]
    public async Task Repeating_a_withdraw_changes_nothing_the_second_time()
    {
        var draft = await CreateReadyDraftAsync();
        var submitted = await ReadAsync(await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) }));
        var version = VersionOf(submitted);

        var first = await WithdrawAsync(IdOf(draft), new { version });
        var second = await WithdrawAsync(IdOf(draft), new { version });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, _api.Audit.Events.Count(e => e.Action == AuditActions.ArticleWithdrawn));
    }

    [Fact]
    public async Task A_withdraw_racing_an_administrator_decision_lets_exactly_one_succeed()
    {
        for (var round = 0; round < 10; round++)
        {
            var draft = await CreateReadyDraftAsync($"Decision race {round}");
            var submitted = await ReadAsync(await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) }));
            var id = IdOf(draft);

            // What the review endpoint will do (F10.2): move the article only while it is still PendingReview.
            var withdraw = WithdrawAsync(id, new { version = VersionOf(submitted) });
            var approve = _api.ScalarAsync<long>(
                "WITH u AS (UPDATE articles SET status = 'Published', published_at = now() " +
                "WHERE id = @id AND status = 'PendingReview' RETURNING 1) SELECT count(*) FROM u", ("id", id));
            var response = await withdraw;
            var approved = await approve;

            var withdrawWon = response.StatusCode == HttpStatusCode.OK;
            Assert.True(withdrawWon ^ (approved == 1), $"round {round}: withdraw {response.StatusCode}, approve rows {approved}");
            if (withdrawWon)
            {
                Assert.Equal("Draft", await ColumnAsync(id, "status"));
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("Published", await ColumnAsync(id, "status"));
                Assert.Equal("<null>", await SubmissionColumnAsync(id, 1, "withdrawn_at"));
            }
        }
    }

    // ---- who may call ----

    [Fact]
    public async Task Nobody_can_submit_or_withdraw_somebody_elses_article()
    {
        var draft = await CreateReadyDraftAsync();

        var submit = await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) }, _bobToken);
        var withdraw = await WithdrawAsync(IdOf(draft), new { version = VersionOf(draft) }, _bobToken);
        var unknown = await SubmitAsync(Guid.NewGuid(), new { version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withdraw.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("Draft", await ColumnAsync(IdOf(draft), "status"));
        Assert.Empty(_api.Audit.Events);
    }

    [Fact]
    public async Task Administrators_get_403_and_visitors_401()
    {
        var draft = await CreateReadyDraftAsync();
        var id = IdOf(draft);
        var body = new { version = VersionOf(draft) };

        Assert.Equal(HttpStatusCode.Forbidden, (await SubmitAsync(id, body, _adminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await WithdrawAsync(id, body, _adminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{id}/submit", null, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{id}/withdraw", null, body)).StatusCode);
        Assert.Equal("Draft", await ColumnAsync(id, "status"));
    }

    [Fact]
    public async Task A_suspended_author_cannot_submit_or_withdraw()
    {
        var draft = await CreateReadyDraftAsync();
        var submitted = await ReadAsync(await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) }));
        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended', suspension_reason = 'Spam links', suspended_at = now() WHERE id = @id", ("id", _ann.Id));
        using var scope = _api.Services.CreateScope();
        var articles = scope.ServiceProvider.GetRequiredService<IArticleService>();

        var viaApi = await WithdrawAsync(IdOf(draft), new { version = VersionOf(submitted) });

        Assert.Equal(HttpStatusCode.Unauthorized, viaApi.StatusCode);
        await Assert.ThrowsAsync<ForbiddenException>(() => articles.WithdrawAsync(_ann.Id, IdOf(draft), VersionOf(submitted)));
        await Assert.ThrowsAsync<ForbiddenException>(() => articles.SubmitAsync(_ann.Id, IdOf(draft), VersionOf(submitted)));
        Assert.Equal("PendingReview", await ColumnAsync(IdOf(draft), "status"));
    }

    [Fact]
    public async Task A_failure_while_recording_the_audit_event_leaves_the_article_unchanged()
    {
        var draft = await CreateReadyDraftAsync();
        _api.Audit.FailOn = AuditActions.ArticleSubmitted;

        var response = await SubmitAsync(IdOf(draft), new { version = VersionOf(draft) });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Draft", await ColumnAsync(IdOf(draft), "status"));
        Assert.Equal(0, await SubmissionCountAsync(IdOf(draft)));
    }
}
