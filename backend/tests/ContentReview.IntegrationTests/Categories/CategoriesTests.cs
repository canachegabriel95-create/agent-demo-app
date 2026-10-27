using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Categories;

[Collection(PostgresCollection.Name)]
public class CategoriesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] Names = ["Technology", "Science", "Business", "Health", "Culture", "Education"];

    private ApiHost _api = null!;
    private string _adminToken = null!;
    private string _authorToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator, fullName: "Alice Admin");
        await _api.SeedUserAsync("ann@demo.local", UserRole.Author, fullName: "Ann Author");
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _authorToken = await _api.LoginTokenAsync("ann@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private Task<HttpResponseMessage> ListAsync(string? token) => _api.SendAsync(HttpMethod.Get, "/api/categories", token);

    // ---- the data ----

    [Fact]
    public async Task The_database_has_the_six_categories_with_stable_ids_and_sort_order_after_migration()
    {
        var rows = new List<(Guid Id, string Name, string Slug, int Sort)>();
        foreach (var entry in CategorySeed.All)
        {
            rows.Add((
                await _api.ScalarAsync<Guid>("SELECT id FROM categories WHERE slug = @slug", ("slug", entry.Slug)),
                await _api.ScalarAsync<string>("SELECT name FROM categories WHERE slug = @slug", ("slug", entry.Slug)),
                entry.Slug,
                await _api.ScalarAsync<int>("SELECT sort_order FROM categories WHERE slug = @slug", ("slug", entry.Slug))));
        }

        Assert.Equal(6, await _api.ScalarAsync<long>("SELECT count(*) FROM categories"));
        Assert.Equal(
            CategorySeed.All.Select(c => (c.Id, c.Name, c.Slug, c.SortOrder)),
            rows);
    }

    [Fact]
    public void The_seed_ids_are_fixed_values_written_down_here_so_a_change_is_noticed()
    {
        Assert.Equal(
            [
                "0197a000-0000-7000-8000-000000000001",
                "0197a000-0000-7000-8000-000000000002",
                "0197a000-0000-7000-8000-000000000003",
                "0197a000-0000-7000-8000-000000000004",
                "0197a000-0000-7000-8000-000000000005",
                "0197a000-0000-7000-8000-000000000006",
            ],
            CategorySeed.All.Select(c => c.Id.ToString()));
        Assert.Equal(Names, CategorySeed.All.Select(c => c.Name));
        Assert.Equal([1, 2, 3, 4, 5, 6], CategorySeed.All.Select(c => c.SortOrder));
    }

    // ---- GET /api/categories ----

    [Fact]
    public async Task An_author_gets_the_six_categories_in_display_order()
    {
        var response = await ListAsync(_authorToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await ReadAsync(response)).EnumerateArray().ToList();
        Assert.Equal(Names, items.Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(
            ["technology", "science", "business", "health", "culture", "education"],
            items.Select(i => i.GetProperty("slug").GetString()));
        Assert.Equal(CategorySeed.All.Select(c => c.Id), items.Select(i => i.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task An_administrator_gets_the_same_list()
    {
        var author = await ReadAsync(await ListAsync(_authorToken));
        var admin = await ReadAsync(await ListAsync(_adminToken));

        Assert.Equal(author.GetRawText(), admin.GetRawText());
    }

    [Fact]
    public async Task Each_item_has_only_id_name_and_slug()
    {
        var first = (await ReadAsync(await ListAsync(_authorToken)))[0];

        Assert.Equal(["id", "name", "slug"], first.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task The_order_follows_sort_order_not_the_alphabet_or_the_insert_order()
    {
        await _api.ExecuteAsync("UPDATE categories SET sort_order = 10 WHERE slug = 'technology'");
        await _api.ExecuteAsync("UPDATE categories SET sort_order = 0 WHERE slug = 'education'");

        var names = (await ReadAsync(await ListAsync(_authorToken))).EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();

        Assert.Equal(["Education", "Science", "Business", "Health", "Culture", "Technology"], names);
    }

    [Fact]
    public async Task The_response_may_be_cached_privately_for_five_minutes()
    {
        var response = await ListAsync(_authorToken);

        var cache = response.Headers.CacheControl!;
        Assert.True(cache.Private);
        Assert.False(cache.Public);
        Assert.Equal(TimeSpan.FromSeconds(300), cache.MaxAge);
        // The order of the directives is not significant: compare them as a set.
        var directives = response.Headers.GetValues("Cache-Control").SelectMany(v => v.Split(',')).Select(d => d.Trim());
        Assert.Equal(["max-age=300", "private"], directives.Order());
    }

    [Fact]
    public async Task The_response_carries_the_correlation_id()
    {
        var response = await ListAsync(_authorToken);

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    // ---- who may call it ----

    [Fact]
    public async Task No_token_or_a_bad_token_gets_401_and_nothing_is_cached()
    {
        var anonymous = await ListAsync(null);
        var bad = await ListAsync("not-a-real-token");
        var wrongScheme = await _api.SendAsync(HttpMethod.Get, "/api/categories", _authorToken, scheme: "Basic");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongScheme.StatusCode);
        Assert.Null(anonymous.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task A_suspended_authors_token_gets_401()
    {
        await _api.ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE normalized_email = 'ann@demo.local'");

        var response = await ListAsync(_authorToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_out_token_gets_401()
    {
        await _api.SendAsync(HttpMethod.Post, "/api/auth/logout", _authorToken);

        var response = await ListAsync(_authorToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- nothing can change the list ----

    [Theory]
    [InlineData("POST", "/api/categories")]
    [InlineData("PUT", "/api/categories")]
    [InlineData("PATCH", "/api/categories")]
    [InlineData("DELETE", "/api/categories")]
    [InlineData("GET", "/api/categories/0197a000-0000-7000-8000-000000000001")]
    [InlineData("PUT", "/api/categories/0197a000-0000-7000-8000-000000000001")]
    [InlineData("PATCH", "/api/categories/0197a000-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/categories/0197a000-0000-7000-8000-000000000001")]
    [InlineData("POST", "/api/admin/categories")]
    [InlineData("PUT", "/api/admin/categories/0197a000-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/admin/categories/0197a000-0000-7000-8000-000000000001")]
    public async Task There_is_no_way_to_create_edit_or_delete_a_category_even_as_an_administrator(string method, string path)
    {
        foreach (var token in new[] { _adminToken, _authorToken })
        {
            var response = await _api.SendJsonAsync(
                new HttpMethod(method), path, token, new { name = "Hacked", slug = "hacked", sortOrder = 99 });

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Forbidden,
                $"{method} {path} returned {(int)response.StatusCode}");
        }

        Assert.Equal(6, await _api.ScalarAsync<long>("SELECT count(*) FROM categories"));
        Assert.Equal(0, await _api.ScalarAsync<long>("SELECT count(*) FROM categories WHERE name = 'Hacked'"));
        Assert.Equal(
            "Technology",
            await _api.ScalarAsync<string>("SELECT name FROM categories WHERE id = '0197a000-0000-7000-8000-000000000001'"));
    }
}
