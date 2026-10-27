using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Auditing;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Auditing;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentReview.IntegrationTests.Auditing;

/// <summary>The real audit writer against the real database: one event per action, in the same transaction.</summary>
[Collection(PostgresCollection.Name)]
public class AuditEventTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string Summary = new('s', 30);
    private static readonly string Body = new('b', 150);

    private ApiHost _api = null!;
    private User _admin = null!;
    private User _ann = null!;
    private string _adminToken = null!;
    private string _annToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres, realAudit: true);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _ann = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _annToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    // ---- helpers ----

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private record Row(Guid? Actor, string Action, string EntityType, Guid? EntityId, JsonElement Details, string? CorrelationId, DateTimeOffset OccurredAt);

    private async Task<List<Row>> EventsAsync(string? action = null)
    {
        await using var connection = new Npgsql.NpgsqlConnection(_api.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT actor_user_id, action, entity_type, entity_id, coalesce(details::text, 'null'), correlation_id, occurred_at " +
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
                JsonDocument.Parse(reader.GetString(4)).RootElement.Clone(),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return rows;
    }

    private async Task<Row> OnlyAsync(string action)
    {
        var rows = await EventsAsync(action);
        return Assert.Single(rows);
    }

    private async Task<JsonElement> CreateInvitationAsync(string email = "newbie@demo.local")
    {
        var response = await _api.SendJsonAsync(HttpMethod.Post, "/api/admin/invitations", _adminToken, new { email, validForDays = 7 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement> CreateArticleAsync(string title)
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, "/api/articles", _annToken,
            new { title, summary = Summary, body = Body, categoryId = CategorySeed.Science.Id });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement> SubmitAsync(JsonElement article)
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/articles/{article.GetProperty("id").GetGuid()}/submit", _annToken,
            new { version = article.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<HttpResponseMessage> DecideAsync(JsonElement submitted, bool approve) =>
        await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/articles/{submitted.GetProperty("id").GetGuid()}/{(approve ? "approve" : "reject")}", _adminToken,
            new
            {
                version = submitted.GetProperty("version").GetUInt32(),
                submissionVersion = submitted.GetProperty("submissionVersion").GetInt32(),
                reason = "Please add concrete examples.",
            });

    private async Task<uint> UserVersionAsync() =>
        (await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"/api/admin/users/{_ann.Id}", _adminToken))).GetProperty("version").GetUInt32();

    // ---- one event per action ----

    [Fact]
    public async Task Creating_an_invitation_writes_one_event_with_the_code_suffix_only()
    {
        var response = await _api.SendJsonAsync(HttpMethod.Post, "/api/admin/invitations", _adminToken, new { email = "newbie@demo.local", validForDays = 7 });
        var invitation = await ReadAsync(response);
        var code = invitation.GetProperty("code").GetString()!;

        var audit = await OnlyAsync(AuditActions.InvitationCreated);

        Assert.Equal(_admin.Id, audit.Actor);
        Assert.Equal("Invitation", audit.EntityType);
        Assert.Equal(invitation.GetProperty("id").GetGuid(), audit.EntityId);
        Assert.Equal("newbie@demo.local", audit.Details.GetProperty("email").GetString());
        Assert.Equal(code[^4..], audit.Details.GetProperty("codeSuffix").GetString());
        Assert.DoesNotContain(code, audit.Details.GetRawText());
        Assert.DoesNotContain(code.Replace("-", ""), audit.Details.GetRawText());
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), audit.CorrelationId);
        Assert.True(Math.Abs((audit.OccurredAt - _api.Clock.GetUtcNow()).TotalSeconds) < 5);
    }

    [Fact]
    public async Task Revoking_an_invitation_writes_one_event()
    {
        var invitation = await CreateInvitationAsync();
        var response = await _api.SendJsonAsync(
            HttpMethod.Post, $"/api/admin/invitations/{invitation.GetProperty("id").GetGuid()}/revoke", _adminToken,
            new { version = invitation.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var audit = await OnlyAsync(AuditActions.InvitationRevoked);

        Assert.Equal(_admin.Id, audit.Actor);
        Assert.Equal(invitation.GetProperty("id").GetGuid(), audit.EntityId);
        Assert.DoesNotContain(invitation.GetProperty("code").GetString()!, audit.Details.GetRawText());
    }

    [Fact]
    public async Task Registering_writes_one_event_by_the_new_user_and_never_the_password()
    {
        var invitation = await CreateInvitationAsync("newbie@demo.local");
        var registration = await _api.SendJsonAsync(HttpMethod.Post, "/api/registration", null, new
        {
            invitationCode = invitation.GetProperty("code").GetString(),
            fullName = "New Writer",
            email = "newbie@demo.local",
            password = "Wr1ter!Passw0rd#5",
            confirmPassword = "Wr1ter!Passw0rd#5",
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var user = await ReadAsync(registration);

        var audit = await OnlyAsync(AuditActions.UserRegistered);

        Assert.Equal(user.GetProperty("id").GetGuid(), audit.Actor);
        Assert.Equal("User", audit.EntityType);
        Assert.Equal(user.GetProperty("id").GetGuid(), audit.EntityId);
        Assert.Equal(invitation.GetProperty("id").GetGuid(), audit.Details.GetProperty("invitationId").GetGuid());
        Assert.DoesNotContain("Wr1ter", audit.Details.GetRawText());
    }

    [Fact]
    public async Task Editing_suspending_and_reactivating_an_author_each_write_one_event()
    {
        var edit = await _api.SendJsonAsync(HttpMethod.Put, $"/api/admin/users/{_ann.Id}/profile", _adminToken,
            new { fullName = "Ann Renamed", bio = "Hello", version = await UserVersionAsync() });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var suspend = await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/users/{_ann.Id}/suspend", _adminToken,
            new { reason = "Posting spam links", version = await UserVersionAsync() });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        var reactivate = await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/users/{_ann.Id}/reactivate", _adminToken,
            new { version = await UserVersionAsync() });
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);

        var edited = await OnlyAsync(AuditActions.AdminProfileEdited);
        var suspended = await OnlyAsync(AuditActions.UserSuspended);
        var reactivated = await OnlyAsync(AuditActions.UserReactivated);

        foreach (var audit in new[] { edited, suspended, reactivated })
        {
            Assert.Equal(_admin.Id, audit.Actor);
            Assert.Equal("User", audit.EntityType);
            Assert.Equal(_ann.Id, audit.EntityId);
            Assert.False(string.IsNullOrEmpty(audit.CorrelationId));
        }

        Assert.Equal("Posting spam links", suspended.Details.GetProperty("reason").GetString());
        Assert.Contains("fullName", edited.Details.GetProperty("changedFields").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task Submitting_withdrawing_approving_and_rejecting_each_write_one_event()
    {
        var submitted = await SubmitAsync(await CreateArticleAsync("To be withdrawn"));
        var withdrawn = await _api.SendJsonAsync(HttpMethod.Post, $"/api/articles/{submitted.GetProperty("id").GetGuid()}/withdraw", _annToken,
            new { version = submitted.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);

        var toApprove = await SubmitAsync(await CreateArticleAsync("To be approved"));
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(toApprove, approve: true)).StatusCode);
        var toReject = await SubmitAsync(await CreateArticleAsync("To be rejected"));
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(toReject, approve: false)).StatusCode);

        Assert.Equal(3, (await EventsAsync(AuditActions.ArticleSubmitted)).Count);
        var withdrawal = await OnlyAsync(AuditActions.ArticleWithdrawn);
        var approval = await OnlyAsync(AuditActions.ArticleApproved);
        var rejection = await OnlyAsync(AuditActions.ArticleRejected);

        Assert.Equal(_ann.Id, withdrawal.Actor);
        Assert.Equal(submitted.GetProperty("id").GetGuid(), withdrawal.EntityId);
        Assert.Equal(_admin.Id, approval.Actor);
        Assert.Equal(toApprove.GetProperty("id").GetGuid(), approval.EntityId);
        Assert.Equal("Article", approval.EntityType);
        Assert.Equal(_admin.Id, rejection.Actor);
        Assert.Equal("Please add concrete examples.", rejection.Details.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_failed_or_refused_action_writes_nothing()
    {
        var submitted = await SubmitAsync(await CreateArticleAsync("Decided twice"));
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(submitted, approve: true)).StatusCode);

        var again = await DecideAsync(submitted, approve: true);
        var invalid = await _api.SendJsonAsync(HttpMethod.Post, "/api/admin/invitations", _adminToken, new { email = "not-an-email", validForDays = 7 });
        var forbidden = await _api.SendJsonAsync(HttpMethod.Post, "/api/admin/invitations", _annToken, new { email = "x@demo.local", validForDays = 7 });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Single(await EventsAsync(AuditActions.ArticleApproved));
        Assert.Empty(await EventsAsync(AuditActions.InvitationCreated));
    }

    // ---- same transaction ----

    [Fact]
    public async Task A_rolled_back_transaction_leaves_no_audit_event()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentReviewDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            audit.Record(AuditActions.UserSuspended, nameof(User), _ann.Id, _admin.Id, new { reason = "Rolled back" });
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.AuditEvents.CountAsync());
            await transaction.RollbackAsync();
        }

        Assert.Empty(await EventsAsync());
    }

    [Fact]
    public async Task A_business_change_that_fails_to_save_takes_its_audit_event_with_it()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentReviewDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        // The same email as an existing user: the unique index rejects the insert.
        db.Users.Add(User.Create("ann@demo.local", "hash", "Duplicate Ann", UserRole.Author, _api.Clock.GetUtcNow()));
        audit.Record(AuditActions.UserRegistered, nameof(User), null, null, new { email = "ann@demo.local" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Empty(await EventsAsync());
    }

    // ---- no secrets ----

    [Fact]
    public async Task No_audit_event_ever_contains_a_password_hash_session_token_or_full_invitation_code()
    {
        const string password = "Wr1ter!Passw0rd#5";
        var invitation = await CreateInvitationAsync("newbie@demo.local");
        var code = invitation.GetProperty("code").GetString()!;
        await _api.SendJsonAsync(HttpMethod.Post, "/api/registration", null, new
        {
            invitationCode = code, fullName = "New Writer", email = "newbie@demo.local", password, confirmPassword = password,
        });
        var newbieToken = await _api.LoginTokenAsync("newbie@demo.local", "Author", password);
        await SubmitAsync(await CreateArticleAsync("Anything"));
        await _api.SendJsonAsync(HttpMethod.Post, $"/api/admin/users/{_ann.Id}/suspend", _adminToken,
            new { reason = "Posting spam links", version = await UserVersionAsync() });

        var everything = string.Join("\n", (await EventsAsync()).Select(e => e.Details.GetRawText()));
        var storedHashes = (await _api.ScalarAsync<string>("SELECT string_agg(password_hash, ' ') FROM users")).Split(' ');

        Assert.NotEmpty(await EventsAsync());
        Assert.DoesNotContain("Wr1ter", everything);
        Assert.DoesNotContain(ApiHost.Password, everything);
        Assert.DoesNotContain(code, everything);
        Assert.DoesNotContain(code.Replace("-", ""), everything);
        foreach (var hash in storedHashes)
        {
            Assert.DoesNotContain(hash, everything);
        }

        foreach (var token in new[] { _adminToken, _annToken, newbieToken })
        {
            Assert.DoesNotContain(token, everything);
        }
    }

    // ---- append-only ----

    [Fact]
    public async Task An_audit_event_cannot_be_modified_or_deleted_through_the_application()
    {
        await CreateInvitationAsync();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentReviewDbContext>();
        var stored = await db.AuditEvents.SingleAsync();

        db.Entry(stored).Property(e => e.Action).CurrentValue = "Tampered";
        await Assert.ThrowsAsync<AuditImmutableException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var again = await db.AuditEvents.SingleAsync();
        db.AuditEvents.Remove(again);
        await Assert.ThrowsAsync<AuditImmutableException>(() => db.SaveChangesAsync());

        var row = Assert.Single(await EventsAsync());
        Assert.Equal(AuditActions.InvitationCreated, row.Action);
    }

    [Fact]
    public async Task There_is_no_endpoint_that_writes_audit_events()
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            var response = await _api.SendJsonAsync(method, "/api/admin/audit-events", _adminToken, new { action = "Forged" });
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        }

        Assert.Empty(await EventsAsync());
    }
}
