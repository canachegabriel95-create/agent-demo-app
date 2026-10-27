using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Api.OpenApi;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.OpenApi;

/// <summary>Swagger UI and the OpenAPI document. No database is needed: the host is only asked for its documentation.</summary>
public class SwaggerTests
{
    private const string Unused = "Host=localhost;Database=unused;Username=unused;Password=unused";

    private static ApiFactory Host(string environment) =>
        new(Unused, new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" }, environment);

    private static async Task<JsonElement> DocumentAsync(ApiFactory factory)
    {
        var response = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Operation(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method);

    // ---- where it is served ----

    [Theory]
    [InlineData("Development")]
    [InlineData("Demo")]
    public async Task Swagger_ui_and_the_document_are_served_in_development_and_demo(string environment)
    {
        await using var factory = Host(environment);
        var client = factory.CreateClient();

        var ui = await client.GetAsync("/swagger/index.html");
        var document = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("swagger", (await ui.Content.ReadAsStringAsync()).ToLowerInvariant());
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal("application/json", document.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Swagger_redirects_to_its_ui()
    {
        await using var factory = Host("Development");
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/swagger");

        Assert.True(response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect);
        Assert.Contains("swagger/index.html", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/swagger/index.html")]
    public async Task Production_does_not_serve_swagger_and_does_not_confirm_it_exists(string url)
    {
        await using var factory = Host("Production");

        var response = await factory.CreateClient().GetAsync(url);

        // Like any URL that is not an endpoint, an anonymous caller is asked to sign in.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- authentication ----

    [Fact]
    public async Task The_document_defines_an_http_bearer_scheme()
    {
        await using var factory = Host("Development");

        var document = await DocumentAsync(factory);

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Protected_operations_require_the_bearer_token_and_document_401_and_403()
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);

        foreach (var (path, method) in new[] { ("/api/auth/me", "get"), ("/api/admin/users", "get"), ("/api/articles", "post"), ("/api/feed", "get") })
        {
            var operation = Operation(document, path, method);
            var security = operation.GetProperty("security");
            Assert.True(security[0].TryGetProperty("Bearer", out _), $"{method} {path} has no bearer requirement");
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out var unauthorized), $"{method} {path} has no 401");
            Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _), $"{method} {path} has no 403");
            Assert.True(unauthorized.GetProperty("content").TryGetProperty("application/problem+json", out _));
        }
    }

    [Fact]
    public async Task Successes_are_json_and_every_error_is_problem_json()
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);

        var wrong = new List<string>();
        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                foreach (var response in operation.Value.GetProperty("responses").EnumerateObject())
                {
                    if (!response.Value.TryGetProperty("content", out var content))
                    {
                        continue;
                    }

                    var types = content.EnumerateObject().Select(t => t.Name).ToList();
                    // /health answers JSON even when it reports a failure (503): the body names the failing check.
                    var expected = response.Name[0] is '4' or '5' && path.Name != "/health" ? "application/problem+json" : "application/json";
                    if (types.Count != 1 || types[0] != expected)
                    {
                        wrong.Add($"{operation.Name.ToUpperInvariant()} {path.Name} {response.Name}: {string.Join(", ", types)}");
                    }
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    [Fact]
    public async Task Anonymous_operations_have_no_security_requirement()
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);

        foreach (var (path, method) in new[] { ("/api/auth/login", "post"), ("/api/registration", "post"), ("/api/registration/validate-invitation", "post"), ("/health", "get") })
        {
            var operation = Operation(document, path, method);
            Assert.False(
                operation.TryGetProperty("security", out var security) && security.GetArrayLength() > 0,
                $"{method} {path} must stay anonymous");
        }
    }

    // ---- content ----

    [Theory]
    [InlineData("/api/articles/{articleId}/revision", "post", "Articles", new[] { "201", "401", "403", "404", "409" })]
    [InlineData("/api/articles/{articleId}/revision", "put", "Articles", new[] { "200", "400", "404", "409" })]
    [InlineData("/api/articles/{articleId}/revision/submit", "post", "Articles", new[] { "200", "400", "404", "409" })]
    [InlineData("/api/articles/{articleId}/revision/withdraw", "post", "Articles", new[] { "200", "404", "409" })]
    [InlineData("/api/admin/revisions/{id}", "get", "Admin – Reviews", new[] { "200", "403", "404" })]
    [InlineData("/api/admin/revisions/{id}/approve", "post", "Admin – Reviews", new[] { "200", "400", "403", "404", "409" })]
    [InlineData("/api/admin/revisions/{id}/reject", "post", "Admin – Reviews", new[] { "200", "400", "403", "404", "409" })]
    public async Task The_revision_endpoints_are_documented_with_their_status_codes(
        string path, string method, string tag, string[] statuses)
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);

        var operation = Operation(document, path, method);

        Assert.Equal(tag, operation.GetProperty("tags")[0].GetString());
        var responses = operation.GetProperty("responses").EnumerateObject().Select(r => r.Name);
        Assert.All(statuses, status => Assert.Contains(status, responses));
    }

    [Fact]
    public async Task Operations_are_grouped_by_the_documented_tags()
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);

        var tags = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .SelectMany(operation => operation.Value.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!))
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal(ApiTags.All.Order(), tags);
        Assert.Equal("Auth", Operation(document, "/api/auth/login", "post").GetProperty("tags")[0].GetString());
        Assert.Equal("Admin – Users", Operation(document, "/api/admin/users", "get").GetProperty("tags")[0].GetString());
        Assert.Equal("Admin – Dashboard & Audit", Operation(document, "/api/admin/dashboard", "get").GetProperty("tags")[0].GetString());
        Assert.Equal("Me", Operation(document, "/api/me/articles", "get").GetProperty("tags")[0].GetString());
        Assert.Equal("Health", Operation(document, "/health", "get").GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Every_operation_has_a_summary_from_its_xml_comment()
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);

        var missing = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(op => (Path: path.Name, Method: op.Name, Operation: op.Value)))
            .Where(x => !x.Operation.TryGetProperty("summary", out var summary) || string.IsNullOrWhiteSpace(summary.GetString()))
            .Select(x => $"{x.Method.ToUpperInvariant()} {x.Path}")
            .ToList();

        Assert.True(missing.Count == 0, $"Operations without a summary: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task Enums_are_strings_in_the_schemas()
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);

        var login = document.GetProperty("components").GetProperty("schemas").GetProperty("LoginRequest").GetProperty("properties").GetProperty("portal");
        var text = login.GetRawText();

        Assert.Contains("\"string\"", text);
        Assert.Contains("Admin", text);
        Assert.Contains("Author", text);
        Assert.DoesNotContain("\"integer\"", text);
    }

    [Fact]
    public async Task Examples_use_demo_data_only()
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);
        var schemas = document.GetProperty("components").GetProperty("schemas");

        var login = schemas.GetProperty("LoginRequest").GetProperty("example");
        var register = schemas.GetProperty("RegisterRequest").GetProperty("example");

        Assert.EndsWith("@demo.local", login.GetProperty("email").GetString());
        Assert.EndsWith("@demo.local", register.GetProperty("email").GetString());
        Assert.Equal(DemoExamplesSchemaFilter.ExamplePassword, login.GetProperty("password").GetString());
        Assert.Equal(DemoExamplesSchemaFilter.ExampleInvitationCode, register.GetProperty("invitationCode").GetString());
    }

    [Fact]
    public async Task The_document_covers_the_main_areas()
    {
        await using var factory = Host("Development");
        var document = await DocumentAsync(factory);
        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        foreach (var expected in new[]
                 {
                     "/api/auth/login", "/api/auth/logout", "/api/auth/me", "/api/registration", "/api/admin/invitations",
                     "/api/admin/users/{id}/suspend", "/api/admin/reviews/queue", "/api/admin/audit-events", "/api/me/profile",
                     "/api/articles/{id}/submit", "/api/me/articles/stats", "/api/feed/{id}", "/api/categories", "/health",
                 })
        {
            Assert.Contains(expected, paths);
        }
    }
}
