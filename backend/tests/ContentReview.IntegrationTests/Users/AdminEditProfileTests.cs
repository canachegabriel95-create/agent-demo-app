using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Users;

[Collection(PostgresCollection.Name)]
public class AdminEditProfileTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private Domain.Entities.User _author = null!;
    private string _adminToken = null!;
    private string _authorToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        _author = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _authorToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
        await _api.ExecuteAsync("UPDATE users SET bio = 'Old bio' WHERE id = @id", ("id", _author.Id));
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> GetAsync(Guid? id = null) =>
        await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"/api/admin/users/{id ?? _author.Id}", _adminToken));

    private async Task<uint> VersionAsync(Guid? id = null) => (await GetAsync(id)).GetProperty("version").GetUInt32();

    private Task<HttpResponseMessage> PutAsync(object body, Guid? id = null, string? token = null) =>
        _api.SendJsonAsync(HttpMethod.Put, $"/api/admin/users/{id ?? _author.Id}/profile", token ?? _adminToken, body);

    private async Task<HttpResponseMessage> EditAsync(string? fullName, string? bio) =>
        await PutAsync(new { fullName, bio, version = await VersionAsync() });

    private Task<string> ColumnAsync(string column, Guid? id = null) =>
        _api.ScalarAsync<string>($"SELECT coalesce({column}::text, '<null>') FROM users WHERE id = @id", ("id", id ?? _author.Id));

    // ---- success ----

    [Fact]
    public async Task A_valid_edit_returns_the_updated_details_and_persists()
    {
        _api.Clock.Advance(TimeSpan.FromHours(1));
        var before = await VersionAsync();

        var response = await PutAsync(new { fullName = "  Ann B. Author ", bio = "Writes about testing.", version = before });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Ann B. Author", body.GetProperty("fullName").GetString());
        Assert.Equal("Writes about testing.", body.GetProperty("bio").GetString());
        Assert.Equal("ann@demo.local", body.GetProperty("email").GetString());
        Assert.NotEqual(before, body.GetProperty("version").GetUInt32());
        Assert.Equal("Ann B. Author", await ColumnAsync("full_name"));
        Assert.Equal("Writes about testing.", await ColumnAsync("bio"));
        Assert.Equal(_api.Clock.GetUtcNow(), new DateTimeOffset(
            await _api.ScalarAsync<DateTime>("SELECT updated_at FROM users WHERE id = @id", ("id", _author.Id)), TimeSpan.Zero));
        Assert.Equal(body.GetProperty("version").GetUInt32(), await VersionAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_biography_clears_it(string? bio)
    {
        var response = await EditAsync("Ann Author", bio);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(response)).GetProperty("bio").ValueKind);
        Assert.Equal("<null>", await ColumnAsync("bio"));
    }

    [Fact]
    public async Task The_limits_are_inclusive()
    {
        var response = await EditAsync(new string('n', 100), new string('b', 1000));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, (await ColumnAsync("full_name")).Length);
        Assert.Equal(1000, (await ColumnAsync("bio")).Length);
    }

    [Fact]
    public async Task A_biography_may_span_several_lines_and_keep_its_text_as_typed()
    {
        var response = await EditAsync("Ann Author", "Line one\nLine two <b>not html</b>");

        Assert.Equal("Line one\nLine two <b>not html</b>", (await ReadAsync(response)).GetProperty("bio").GetString());
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_succeeds_without_a_new_version_or_audit_event()
    {
        var before = await VersionAsync();

        var response = await PutAsync(new { fullName = "Ann Author", bio = "Old bio", version = before });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, (await ReadAsync(response)).GetProperty("version").GetUInt32());
        Assert.Empty(_api.Audit.Events);
    }

    [Fact]
    public async Task A_suspended_author_can_be_edited_and_stays_suspended()
    {
        await _api.ExecuteAsync(
            "UPDATE users SET status = 'Suspended', suspension_reason = 'Spam links', suspended_at = now() WHERE id = @id", ("id", _author.Id));

        var response = await EditAsync("Ann Corrected", "Corrected");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Suspended", body.GetProperty("status").GetString());
        Assert.Equal("Spam links", body.GetProperty("suspensionReason").GetString());
        Assert.Equal("Ann Corrected", body.GetProperty("fullName").GetString());
    }

    // ---- what cannot be changed ----

    [Fact]
    public async Task Email_role_status_password_and_other_properties_in_the_body_are_ignored()
    {
        var hash = await ColumnAsync("password_hash");
        var version = await VersionAsync();

        var response = await PutAsync(new
        {
            fullName = "Ann Renamed",
            bio = "Bio",
            version,
            email = "hacker@evil.example",
            normalizedEmail = "hacker@evil.example",
            role = "Administrator",
            status = "Suspended",
            suspensionReason = "x",
            password = "Hacked!Passw0rd#1",
            passwordHash = "plain",
            id = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("ann@demo.local", body.GetProperty("email").GetString());
        Assert.Equal("Author", body.GetProperty("role").GetString());
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal(_author.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("ann@demo.local", await ColumnAsync("email"));
        Assert.Equal("ann@demo.local", await ColumnAsync("normalized_email"));
        Assert.Equal("Author", await ColumnAsync("role"));
        Assert.Equal("Active", await ColumnAsync("status"));
        Assert.Equal("<null>", await ColumnAsync("suspension_reason"));
        Assert.Equal(hash, await ColumnAsync("password_hash"));
        Assert.Equal(HttpStatusCode.OK, (await _api.LoginAsync("ann@demo.local", ApiHost.Password, "Author")).StatusCode);
    }

    // ---- validation ----

    [Theory]
    [InlineData("", "Full name is required.")]
    [InlineData("   ", "Full name is required.")]
    [InlineData("A", "Full name must be between 2 and 100 characters.")]
    [InlineData("  A  ", "Full name must be between 2 and 100 characters.")]
    public async Task An_invalid_name_is_a_400_and_nothing_changes(string name, string message)
    {
        var response = await EditAsync(name, "Changed bio");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.Equal([message], errors.GetProperty("fullName").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Ann Author", await ColumnAsync("full_name"));
        Assert.Equal("Old bio", await ColumnAsync("bio"));
    }

    [Fact]
    public async Task A_name_over_100_characters_and_a_biography_over_1000_are_reported_together()
    {
        var response = await EditAsync(new string('n', 101), new string('b', 1001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.Equal("Full name must be between 2 and 100 characters.", errors.GetProperty("fullName")[0].GetString());
        Assert.Equal("Biography must be at most 1000 characters.", errors.GetProperty("bio")[0].GetString());
    }

    [Fact]
    public async Task A_missing_name_or_version_is_a_400()
    {
        var noName = await PutAsync(new { bio = "x", version = await VersionAsync() });
        var noVersion = await PutAsync(new { fullName = "Ann Author", bio = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        Assert.True((await ReadAsync(noVersion)).GetProperty("errors").TryGetProperty("version", out _));
    }

    // ---- concurrency ----

    [Fact]
    public async Task A_stale_version_is_a_409_and_changes_nothing()
    {
        var stale = await VersionAsync();
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(new { fullName = "Ann First", bio = "One", version = stale })).StatusCode);

        var response = await PutAsync(new { fullName = "Ann Second", bio = "Two", version = stale });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed by someone else", (await ReadAsync(response)).GetProperty("detail").GetString());
        Assert.Equal("Ann First", await ColumnAsync("full_name"));
        Assert.Equal("One", await ColumnAsync("bio"));
    }

    [Fact]
    public async Task The_new_version_can_be_used_for_the_next_edit()
    {
        var first = await ReadAsync(await PutAsync(new { fullName = "Ann First", bio = "One", version = await VersionAsync() }));

        var second = await PutAsync(new { fullName = "Ann Second", bio = "Two", version = first.GetProperty("version").GetUInt32() });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_edits_with_the_same_version_let_exactly_one_win()
    {
        var version = await VersionAsync();

        var responses = await Task.WhenAll(
            PutAsync(new { fullName = "Ann Alpha", bio = "A", version }),
            PutAsync(new { fullName = "Ann Beta", bio = "B", version }));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Contains(await ColumnAsync("full_name"), new[] { "Ann Alpha", "Ann Beta" });
    }

    // ---- targets ----

    [Theory]
    [InlineData("administrator")]
    [InlineData("unknown")]
    public async Task An_administrator_or_an_unknown_id_is_404_and_an_administrator_is_untouched(string which)
    {
        var id = which == "administrator" ? _admin.Id : Guid.NewGuid();

        var response = await PutAsync(new { fullName = "Hijacked Admin", bio = "x", version = 1 }, id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Alice Admin", await ColumnAsync("full_name", _admin.Id));
        Assert.Empty(_api.Audit.Events);
    }

    [Fact]
    public async Task The_target_is_checked_before_the_body()
    {
        var response = await PutAsync(new { fullName = "", bio = "x", version = 1 }, _admin.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- audit ----

    [Fact]
    public async Task The_audit_event_names_the_actor_target_and_changed_fields_but_not_the_text()
    {
        await EditAsync("Ann Renamed", "A secret biography text.");

        var audit = Assert.Single(_api.Audit.Events);

        Assert.Equal("AdminProfileEdited", audit.Action);
        Assert.Equal("User", audit.EntityType);
        Assert.Equal(_author.Id, audit.EntityId);
        Assert.Equal(_admin.Id, audit.ActorUserId);
        var details = JsonDocument.Parse(audit.DetailsJson).RootElement;
        Assert.Equal(_author.Id, details.GetProperty("targetUserId").GetGuid());
        Assert.Equal(["fullName", "bio"], details.GetProperty("changedFields").EnumerateArray().Select(f => f.GetString()));
        Assert.DoesNotContain("secret", audit.DetailsJson);
        Assert.DoesNotContain("Renamed", audit.DetailsJson);
        Assert.DoesNotContain("Old bio", audit.DetailsJson);
    }

    [Fact]
    public async Task Only_the_fields_that_changed_are_listed()
    {
        await EditAsync("Ann Author", "Brand new bio");

        var details = JsonDocument.Parse(Assert.Single(_api.Audit.Events).DetailsJson).RootElement;

        Assert.Equal(["bio"], details.GetProperty("changedFields").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task A_rejected_edit_writes_no_audit_event()
    {
        await EditAsync("", "x");
        await PutAsync(new { fullName = "Ann Author", bio = "x", version = 1 });

        Assert.Empty(_api.Audit.Events);
    }

    // ---- authorization ----

    [Fact]
    public async Task Authors_get_403_visitors_401_and_nothing_changes()
    {
        var version = await VersionAsync();
        var body = new { fullName = "Self Service", bio = "x", version };

        var author = await PutAsync(body, token: _authorToken);
        var anonymous = await _api.SendJsonAsync(HttpMethod.Put, $"/api/admin/users/{_author.Id}/profile", null, body);

        Assert.Equal(HttpStatusCode.Forbidden, author.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Ann Author", await ColumnAsync("full_name"));
    }
}
