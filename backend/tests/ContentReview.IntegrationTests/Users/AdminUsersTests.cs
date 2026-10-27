using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Users;

[Collection(PostgresCollection.Name)]
public class AdminUsersTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Route = "/api/admin/users";

    private ApiHost _api = null!;
    private Domain.Entities.User _admin = null!;
    private string _adminToken = null!;
    private string _authorToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        _admin = await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        await _api.SeedUserAsync("reader@demo.local", UserRole.Author, fullName: "Reader Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _authorToken = await _api.LoginTokenAsync("reader@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> ListAsync(string query = "")
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}{query}", _adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("fullName").GetString()!).ToArray();

    /// <summary>Seeds authors one minute apart, oldest first.</summary>
    private async Task SeedAsync(params string[] names)
    {
        _api.Clock.Advance(TimeSpan.FromMinutes(1)); // after the author created in InitializeAsync
        foreach (var name in names)
        {
            var email = $"{name.ToLowerInvariant().Replace(' ', '.')}@demo.local";
            await _api.SeedUserAsync(email, UserRole.Author, fullName: name);
            _api.Clock.Advance(TimeSpan.FromMinutes(1));
        }
    }

    private async Task AddArticleAsync(Guid authorId, string status)
    {
        await _api.ExecuteAsync(
            "INSERT INTO articles (id, author_id, title, summary, body, status, submission_version, created_at, updated_at) " +
            "VALUES (@id, @author, 'Title', '', '', @status, 0, now(), now())",
            ("id", Guid.CreateVersion7()), ("author", authorId), ("status", status));
    }

    // ---- list ----

    [Fact]
    public async Task The_list_has_authors_only_never_administrators()
    {
        await SeedAsync("Ann Author", "Bob Author");

        var page = await ListAsync();

        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain("Alice Admin", Names(page));
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Each_item_has_exactly_id_name_email_status_and_registration_date()
    {
        var page = await ListAsync();

        var item = page.GetProperty("items")[0];
        Assert.Equal(
            ["email", "fullName", "id", "registeredAt", "status"],
            item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("reader@demo.local", item.GetProperty("email").GetString());
        Assert.Equal("Active", item.GetProperty("status").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), item.GetProperty("registeredAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task The_response_never_contains_password_data()
    {
        var text = await (await _api.SendAsync(HttpMethod.Get, Route, _adminToken)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", text);
    }

    [Fact]
    public async Task The_default_order_is_newest_registration_first()
    {
        await SeedAsync("Ann Author", "Bob Author", "Cy Author");

        Assert.Equal(["Cy Author", "Bob Author", "Ann Author", "Reader Author"], Names(await ListAsync()));
        Assert.Equal(["Reader Author", "Ann Author", "Bob Author", "Cy Author"], Names(await ListAsync("?direction=asc")));
    }

    [Fact]
    public async Task Sorting_by_name_is_alphabetical_ignoring_case_and_can_be_reversed()
    {
        await SeedAsync("bob Author", "Cy Author", "ann Author");

        Assert.Equal(["ann Author", "bob Author", "Cy Author", "Reader Author"], Names(await ListAsync("?sort=fullName")));
        Assert.Equal(["Reader Author", "Cy Author", "bob Author", "ann Author"], Names(await ListAsync("?sort=fullName&direction=desc")));
    }

    [Fact]
    public async Task Pages_report_the_total_and_cover_every_author_exactly_once()
    {
        await SeedAsync("A1 Author", "A2 Author", "A3 Author", "A4 Author", "A5 Author"); // plus Reader: 6

        var first = await ListAsync("?pageSize=4");
        var second = await ListAsync("?pageSize=4&page=2");
        var beyond = await ListAsync("?pageSize=4&page=9");

        Assert.Equal(6, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(4, Names(first).Length);
        Assert.Equal(2, Names(second).Length);
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(4, second.GetProperty("pageSize").GetInt32());
        Assert.Equal(6, Names(first).Concat(Names(second)).Distinct().Count());
        Assert.Empty(Names(beyond));
        Assert.Equal(6, beyond.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Authors_registered_at_the_same_instant_are_never_repeated_or_skipped_between_pages()
    {
        for (var i = 0; i < 7; i++)
        {
            await _api.SeedUserAsync($"same{i}@demo.local", UserRole.Author, fullName: $"Same {i}"); // identical timestamps
        }

        var seen = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            seen.AddRange(Names(await ListAsync($"?pageSize=2&page={page}")));
        }

        Assert.Equal(8, seen.Count);
        Assert.Equal(8, seen.Distinct().Count());
    }

    // ---- search ----

    [Theory]
    [InlineData("?search=ann", new[] { "Ann Author" })]
    [InlineData("?search=ANN", new[] { "Ann Author" })]
    [InlineData("?search=%20nn%20", new[] { "Ann Author" })]
    [InlineData("?search=author", new[] { "Ann Author", "Bob Author", "Reader Author" })]
    [InlineData("?search=bob.author@DEMO", new[] { "Bob Author" })]
    [InlineData("?search=demo.local", new[] { "Ann Author", "Bob Author", "Reader Author" })]
    [InlineData("?search=nobody", new string[0])]
    [InlineData("?search=", new[] { "Ann Author", "Bob Author", "Reader Author" })]
    public async Task Search_matches_part_of_the_name_or_email_ignoring_case(string query, string[] expected)
    {
        await SeedAsync("Ann Author", "Bob Author");

        Assert.Equal(expected.Order(), Names(await ListAsync(query)).Order());
    }

    [Fact]
    public async Task Search_does_not_find_administrators()
    {
        Assert.Empty(Names(await ListAsync("?search=alice")));
        Assert.Empty(Names(await ListAsync("?search=admin@demo")));
    }

    [Theory]
    [InlineData("?search=100%25", new[] { "100% Fan" })]
    [InlineData("?search=%25", new[] { "100% Fan" })]
    [InlineData("?search=a_b", new[] { "a_b Writer" })]
    [InlineData("?search=_", new[] { "a_b Writer", "Under_score" })]
    [InlineData("?search=%5C", new[] { @"Back\slash" })]
    [InlineData("?search=100__", new string[0])]
    public async Task Percent_underscore_and_backslash_in_the_search_are_ordinary_characters(string query, string[] expected)
    {
        await _api.SeedUserAsync("fan@demo.local", UserRole.Author, fullName: "100% Fan");
        await _api.SeedUserAsync("fan2@demo.local", UserRole.Author, fullName: "100xx Fan");
        await _api.SeedUserAsync("ab@demo.local", UserRole.Author, fullName: "a_b Writer");
        await _api.SeedUserAsync("axb@demo.local", UserRole.Author, fullName: "axb Writer");
        await _api.SeedUserAsync("us@demo.local", UserRole.Author, fullName: "Under_score");
        await _api.SeedUserAsync("bs@demo.local", UserRole.Author, fullName: @"Back\slash");

        Assert.Equal(expected.Order(), Names(await ListAsync(query)).Order());
    }

    [Fact]
    public async Task Search_also_treats_wildcards_literally_in_emails()
    {
        await _api.SeedUserAsync("first_last@demo.local", UserRole.Author, fullName: "Underscore Mail");
        await _api.SeedUserAsync("firstxlast@demo.local", UserRole.Author, fullName: "Plain Mail");

        Assert.Equal(["Underscore Mail"], Names(await ListAsync("?search=first_last")));
    }

    // ---- status filter ----

    [Fact]
    public async Task The_status_filter_returns_only_that_status()
    {
        await _api.SeedUserAsync("sus@demo.local", UserRole.Author, fullName: "Sus Pended", suspended: true);
        await SeedAsync("Ann Author");

        var suspended = await ListAsync("?status=Suspended");
        var active = await ListAsync("?status=Active");

        Assert.Equal(["Sus Pended"], Names(suspended));
        Assert.Equal(1, suspended.GetProperty("totalCount").GetInt32());
        Assert.Equal("Suspended", suspended.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(["Ann Author", "Reader Author"], Names(active).Order());
        Assert.Equal(3, (await ListAsync()).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_and_status_combine()
    {
        await _api.SeedUserAsync("a1@demo.local", UserRole.Author, fullName: "Anna One", suspended: true);
        await _api.SeedUserAsync("a2@demo.local", UserRole.Author, fullName: "Anna Two");

        Assert.Equal(["Anna One"], Names(await ListAsync("?search=anna&status=Suspended")));
        Assert.Equal(["Anna Two"], Names(await ListAsync("?search=anna&status=Active")));
    }

    [Theory]
    [InlineData("?status=Bogus")]
    [InlineData("?status=7")]
    [InlineData("?sort=email")]
    [InlineData("?direction=sideways")]
    [InlineData("?page=0")]
    [InlineData("?page=x")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task Invalid_filters_paging_and_sorting_are_400(string query)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}{query}", _adminToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task A_search_longer_than_100_characters_is_400_and_100_is_fine()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.SendAsync(HttpMethod.Get, $"{Route}?search={new string('a', 101)}", _adminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.SendAsync(HttpMethod.Get, $"{Route}?search={new string('a', 100)}", _adminToken)).StatusCode);
    }

    // ---- details ----

    [Fact]
    public async Task Details_show_the_profile_status_suspension_and_article_counts_per_status()
    {
        var author = await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author", suspended: true);
        var other = await _api.SeedUserAsync("bob@demo.local", UserRole.Author, fullName: "Bob Author");
        await _api.ExecuteAsync(
            "UPDATE users SET bio = 'Writes about testing.', suspension_reason = 'Spam links', suspended_at = @at WHERE id = @id",
            ("at", _api.Clock.GetUtcNow()), ("id", author.Id));
        foreach (var status in new[] { "Draft", "Draft", "Draft", "PendingReview", "Published", "Published", "Rejected" })
        {
            await AddArticleAsync(author.Id, status);
        }

        await AddArticleAsync(other.Id, "Published"); // someone else's article must not be counted

        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}/{author.Id}", _adminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(author.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Ann Author", body.GetProperty("fullName").GetString());
        Assert.Equal("ann@demo.local", body.GetProperty("email").GetString());
        Assert.Equal("Writes about testing.", body.GetProperty("bio").GetString());
        Assert.Equal("Author", body.GetProperty("role").GetString());
        Assert.Equal("Suspended", body.GetProperty("status").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), body.GetProperty("registeredAt").GetDateTimeOffset());
        Assert.Equal(_api.Clock.GetUtcNow(), body.GetProperty("suspendedAt").GetDateTimeOffset());
        Assert.Equal("Spam links", body.GetProperty("suspensionReason").GetString());
        Assert.True(body.GetProperty("version").GetUInt32() > 0);
        var counts = body.GetProperty("articleCounts");
        Assert.Equal(3, counts.GetProperty("draft").GetInt32());
        Assert.Equal(1, counts.GetProperty("pendingReview").GetInt32());
        Assert.Equal(2, counts.GetProperty("published").GetInt32());
        Assert.Equal(1, counts.GetProperty("rejected").GetInt32());
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_author_without_articles_has_zero_counts_and_null_optional_fields()
    {
        var author = await _api.SeedUserAsync("new@demo.local", UserRole.Author, fullName: "New Author");

        var body = await ReadAsync(await _api.SendAsync(HttpMethod.Get, $"{Route}/{author.Id}", _adminToken));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("bio").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("suspendedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("suspensionReason").ValueKind);
        Assert.Equal("Active", body.GetProperty("status").GetString());
        var counts = body.GetProperty("articleCounts");
        Assert.All(new[] { "draft", "pendingReview", "published", "rejected" }, name => Assert.Equal(0, counts.GetProperty(name).GetInt32()));
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("unknown")]
    [InlineData("not-a-guid")]
    public async Task An_administrator_an_unknown_id_and_a_malformed_id_are_all_404(string which)
    {
        var id = which switch
        {
            "administrator" => _admin.Id.ToString(),
            "unknown" => Guid.NewGuid().ToString(),
            _ => "not-a-guid",
        };

        var response = await _api.SendAsync(HttpMethod.Get, $"{Route}/{id}", _adminToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- authorization ----

    [Theory]
    [InlineData("")]
    [InlineData("/00000000-0000-0000-0000-000000000001")]
    public async Task Authors_get_403_and_visitors_401(string path)
    {
        var author = await _api.SendAsync(HttpMethod.Get, Route + path, _authorToken);
        var anonymous = await _api.SendAsync(HttpMethod.Get, Route + path, null);

        Assert.Equal(HttpStatusCode.Forbidden, author.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
