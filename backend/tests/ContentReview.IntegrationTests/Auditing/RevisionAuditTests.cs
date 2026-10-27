using System.Net;
using System.Text.Json;
using ContentReview.Domain.Auditing;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Auditing;

[Collection(PostgresCollection.Name)]
public class RevisionAuditTests(PostgresFixture postgres) : IAsyncLifetime
{
    private RevisionScenario _s = null!;

    public async Task InitializeAsync() => _s = await RevisionScenario.CreateAsync(postgres, realAudit: true);

    public Task DisposeAsync() => _s.DisposeAsync().AsTask();

    private sealed record Row(Guid? Actor, string Action, string EntityType, Guid? EntityId, JsonElement Details);

    private async Task<List<Row>> EventsAsync(string? action = null)
    {
        await using var connection = new Npgsql.NpgsqlConnection(_s.Api.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT actor_user_id, action, entity_type, entity_id, coalesce(details::text, 'null') " +
            "FROM audit_events WHERE (@a::text IS NULL OR action = @a) ORDER BY occurred_at, id";
        command.Parameters.AddWithValue("a", (object?)action ?? DBNull.Value);
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new Row(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                JsonDocument.Parse(reader.GetString(4)).RootElement.Clone()));
        }

        return rows;
    }

    private async Task<List<Row>> RevisionEventsAsync() =>
        (await EventsAsync()).Where(e => e.Action.StartsWith("Revision", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task Every_revision_action_writes_one_event_by_the_actor()
    {
        var articleId = await _s.PublishedArticleAsync("Original title");
        var draft = await _s.DraftRevisionAsync(articleId);
        var revisionId = draft.GetProperty("id").GetGuid();

        var submitted = await RevisionScenario.ReadAsync(await _s.SubmitRevisionAsync(articleId, RevisionScenario.VersionOf(draft)));
        var withdrawn = await RevisionScenario.ReadAsync(await _s.WithdrawRevisionAsync(articleId, RevisionScenario.VersionOf(submitted)));
        var resubmitted = await RevisionScenario.ReadAsync(await _s.SubmitRevisionAsync(articleId, RevisionScenario.VersionOf(withdrawn)));
        var rejected = await _s.RejectRevisionAsync(revisionId, RevisionScenario.VersionOf(resubmitted), 2, "Please add concrete examples.");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var again = await RevisionScenario.ReadAsync(await _s.GetRevisionAsync(articleId));
        var third = await RevisionScenario.ReadAsync(await _s.SubmitRevisionAsync(articleId, RevisionScenario.VersionOf(again)));
        Assert.Equal(HttpStatusCode.OK, (await _s.ApproveRevisionAsync(revisionId, RevisionScenario.VersionOf(third), 3)).StatusCode);

        var events = await RevisionEventsAsync();
        Assert.Equal(
            ["RevisionCreated", "RevisionSubmitted", "RevisionWithdrawn", "RevisionSubmitted", "RevisionRejected", "RevisionSubmitted", "RevisionApproved"],
            events.Select(e => e.Action));
        Assert.All(events, e =>
        {
            Assert.Equal("ArticleRevision", e.EntityType);
            Assert.Equal(revisionId, e.EntityId);
            Assert.Equal(articleId, e.Details.GetProperty("articleId").GetGuid());
            Assert.Equal(revisionId, e.Details.GetProperty("revisionId").GetGuid());
            Assert.Equal(1, e.Details.GetProperty("revisionNumber").GetInt32());
        });
        Assert.All(events.Where(e => e.Action is "RevisionCreated" or "RevisionSubmitted" or "RevisionWithdrawn"), e => Assert.Equal(_s.Ann.Id, e.Actor));
        Assert.All(events.Where(e => e.Action is "RevisionApproved" or "RevisionRejected"), e => Assert.Equal(_s.Admin.Id, e.Actor));
        Assert.Equal([0, 1, 1, 2, 2, 3, 3], events.Select(e => e.Details.GetProperty("submissionVersion").GetInt32()));
        Assert.Equal("Please add concrete examples.", events.Single(e => e.Action == "RevisionRejected").Details.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Events_never_contain_the_content_of_the_revision()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId, "A very distinctive revised title");
        await _s.ApproveRevisionAsync(pending.GetProperty("id").GetGuid(), RevisionScenario.VersionOf(pending), 1);

        foreach (var e in await RevisionEventsAsync())
        {
            var text = e.Details.GetRawText();
            Assert.DoesNotContain("distinctive", text, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('r', 20), text, StringComparison.Ordinal);
            Assert.DoesNotContain("title", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("body", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Refused_or_conflicting_requests_write_no_event()
    {
        var articleId = await _s.PublishedArticleAsync();
        var draft = await _s.DraftRevisionAsync(articleId);
        var before = (await RevisionEventsAsync()).Count;
        var version = RevisionScenario.VersionOf(draft);

        // second revision (409), stale version (409), withdraw a draft (409), foreign author (404), admin (403),
        // submit a revision that breaks the submission rules (400) after making it invalid.
        await _s.CreateRevisionAsync(articleId);
        await _s.SubmitRevisionAsync(articleId, version + 5);
        await _s.WithdrawRevisionAsync(articleId, version);
        await _s.SubmitRevisionAsync(articleId, version, _s.BobToken);
        await _s.CreateRevisionAsync(articleId, _s.AdminToken);
        var invalid = await _s.UpdateRevisionAsync(articleId, new { title = "abc", summary = "", body = "", version });
        var invalidVersion = RevisionScenario.VersionOf(await RevisionScenario.ReadAsync(invalid));
        Assert.Equal(HttpStatusCode.BadRequest, (await _s.SubmitRevisionAsync(articleId, invalidVersion)).StatusCode);

        Assert.Equal(before, (await RevisionEventsAsync()).Count);
    }

    [Fact]
    public async Task A_lost_decision_race_writes_exactly_one_event()
    {
        var articleId = await _s.PublishedArticleAsync();
        var pending = await _s.PendingRevisionAsync(articleId);
        var id = pending.GetProperty("id").GetGuid();
        var version = RevisionScenario.VersionOf(pending);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _s.ApproveRevisionAsync(id, version, 1)));

        Assert.Single(await EventsAsync(AuditActions.RevisionApproved));
    }

    [Fact]
    public async Task The_events_appear_in_the_admin_audit_history_and_authors_cannot_read_it()
    {
        var articleId = await _s.PublishedArticleAsync();
        await _s.PendingRevisionAsync(articleId);

        var response = await _s.Api.SendAsync(
            HttpMethod.Get, "/api/admin/audit-events?action=RevisionCreated&action=RevisionSubmitted", _s.AdminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await RevisionScenario.ReadAsync(response);
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.All(page.GetProperty("items").EnumerateArray(), i => Assert.Equal("ArticleRevision", i.GetProperty("entityType").GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, (await _s.Api.SendAsync(HttpMethod.Get, "/api/admin/audit-events", _s.AnnToken)).StatusCode);
    }
}
