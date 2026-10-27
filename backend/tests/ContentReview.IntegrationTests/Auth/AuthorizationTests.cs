using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ContentReview.IntegrationTests.Auth;

[Collection(PostgresCollection.Name)]
public class AuthorizationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private string _adminToken = null!;
    private string _authorToken = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator);
        await _api.SeedUserAsync("author@demo.local", UserRole.Author);
        _adminToken = await _api.LoginTokenAsync("admin@demo.local", "Admin");
        _authorToken = await _api.LoginTokenAsync("author@demo.local", "Author");
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private string? Token(string who) => who switch
    {
        "none" => null,
        "admin" => _adminToken,
        "author" => _authorToken,
        _ => throw new ArgumentOutOfRangeException(nameof(who)),
    };

    [Theory]
    // Administrator routes: Author 403, anonymous 401.
    [InlineData("/api/admin/_probe", "none", 401)]
    [InlineData("/api/admin/_probe", "author", 403)]
    [InlineData("/api/admin/_probe", "admin", 200)]
    // Author routes: Administrator 403, anonymous 401.
    [InlineData("/api/me/_probe", "none", 401)]
    [InlineData("/api/me/_probe", "admin", 403)]
    [InlineData("/api/me/_probe", "author", 200)]
    // Any authenticated user.
    [InlineData("/api/_probe/authenticated", "none", 401)]
    [InlineData("/api/_probe/authenticated", "admin", 200)]
    [InlineData("/api/_probe/authenticated", "author", 200)]
    // No attribute at all: the fallback policy requires a signed-in user.
    [InlineData("/api/_probe/unmarked", "none", 401)]
    [InlineData("/api/_probe/unmarked", "admin", 200)]
    [InlineData("/api/_probe/unmarked", "author", 200)]
    // Explicitly anonymous.
    [InlineData("/api/_probe/open", "none", 200)]
    [InlineData("/api/_probe/open", "author", 200)]
    // The auth endpoints and health.
    [InlineData("/api/auth/me", "none", 401)]
    [InlineData("/api/auth/me", "author", 200)]
    [InlineData("/health", "none", 200)]
    public async Task Role_matrix(string route, string who, int expected)
    {
        var response = await _api.SendAsync(HttpMethod.Get, route, Token(who));

        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/_probe", "author", 403)]
    [InlineData("/api/me/_probe", "admin", 403)]
    [InlineData("/api/admin/_probe", "none", 401)]
    public async Task Denials_are_problem_details(string route, string who, int status)
    {
        var response = await _api.SendAsync(HttpMethod.Get, route, Token(who));

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(status, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Login_is_reachable_without_a_token()
    {
        var response = await _api.LoginAsync("admin@demo.local", ApiHost.Password, "Admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_role_is_decided_by_the_account_not_by_the_portal_used_to_log_in()
    {
        // An administrator cannot get an Author session, so the Author routes stay closed to them.
        var response = await _api.LoginAsync("admin@demo.local", ApiHost.Password, "Author");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_author_reads_their_own_article_but_gets_404_for_another_authors_article_or_an_unknown_id()
    {
        var otherAuthor = await _api.SeedUserAsync("other@demo.local", UserRole.Author);
        var own = await SeedArticleAsync("author@demo.local");
        var theirs = await SeedArticleAsync(otherAuthor.Email);

        Assert.Equal(HttpStatusCode.OK, (await _api.SendAsync(HttpMethod.Get, $"/api/articles/_probe/{own}", _authorToken)).StatusCode);

        var foreign = await _api.SendAsync(HttpMethod.Get, $"/api/articles/_probe/{theirs}", _authorToken);
        var unknown = await _api.SendAsync(HttpMethod.Get, $"/api/articles/_probe/{Guid.NewGuid()}", _authorToken);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        // Same status and shape as an id that does not exist: the response does not reveal the foreign article.
        Assert.Equal(
            (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString(),
            (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_administrator_cannot_use_the_author_article_route()
    {
        var article = await SeedArticleAsync("author@demo.local");

        var response = await _api.SendAsync(HttpMethod.Get, $"/api/articles/_probe/{article}", _adminToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedArticleAsync(string authorEmail)
    {
        var authorId = await _api.ScalarAsync<Guid>("SELECT id FROM users WHERE normalized_email = @e", ("e", authorEmail.ToLowerInvariant()));
        var article = Article.CreateDraft(authorId, "A title", "A summary", "A body", null, _api.Clock.GetUtcNow());

        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentReview.Infrastructure.Persistence.ContentReviewDbContext>();
        db.Articles.Add(article);
        await db.SaveChangesAsync();
        return article.Id;
    }
}
