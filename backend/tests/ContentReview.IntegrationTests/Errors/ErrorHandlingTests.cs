using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentReview.IntegrationTests.Errors;

[Collection(PostgresCollection.Name)]
public class ErrorHandlingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Probe = "/api/_probe/errors";

    private ApiHost _api = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);
        await _api.SeedUserAsync("admin@demo.local", UserRole.Administrator);
        await _api.SeedUserAsync("author@demo.local", UserRole.Author);
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string HeaderId(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues("X-Correlation-Id"));

    private Task<HttpResponseMessage> GetAsync(string url, string? correlationId = null, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return _api.Client.SendAsync(request);
    }

    // ---- status mapping -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("validation", 400, "One or more validation errors occurred.")]
    [InlineData("notfound", 404, "Resource not found.")]
    [InlineData("forbidden", 403, "You do not have permission to perform this action.")]
    [InlineData("conflict", 409, "The request conflicts with the current state of the resource.")]
    [InlineData("transition", 409, "The request conflicts with the current state of the resource.")]
    [InlineData("concurrency", 409, "The request conflicts with the current state of the resource.")]
    [InlineData("unique", 409, "The request conflicts with the current state of the resource.")]
    [InlineData("crash", 500, "An unexpected error occurred.")]
    public async Task Each_failure_maps_to_its_status_as_problem_details_with_the_correlation_id(
        string kind, int status, string title)
    {
        var response = await GetAsync($"{Probe}/{kind}");

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await BodyAsync(response);
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal(HeaderId(response), body.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        Assert.True(body.TryGetProperty("type", out _));
    }

    [Fact]
    public async Task Validation_errors_are_keyed_by_camel_case_field_name()
    {
        var body = await BodyAsync(await GetAsync($"{Probe}/validation"));

        var errors = body.GetProperty("errors");
        Assert.Equal("Full name is required.", errors.GetProperty("fullName")[0].GetString());
        Assert.Equal(2, errors.GetProperty("bio").GetArrayLength());
        Assert.False(errors.TryGetProperty("FullName", out _));
    }

    [Theory]
    [InlineData("conflict", "The invitation was already used.")]
    [InlineData("transition", "A Draft article cannot be approved.")]
    [InlineData("concurrency", "The resource was changed by someone else. Reload it and try again.")]
    [InlineData("unique", "A resource with the same unique value already exists.")]
    public async Task Conflicts_explain_what_happened_without_internal_details(string kind, string detail)
    {
        var body = await BodyAsync(await GetAsync($"{Probe}/{kind}"));

        Assert.Equal(detail, body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("notfound")]
    [InlineData("forbidden")]
    public async Task Not_found_and_forbidden_carry_no_detail_from_the_exception(string kind)
    {
        var raw = await (await GetAsync($"{Probe}/{kind}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("db.internal", raw);
        Assert.DoesNotContain("someone else", raw);
        Assert.False(JsonDocument.Parse(raw).RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task An_unhandled_exception_returns_a_generic_500_without_message_stack_trace_or_secret()
    {
        var response = await GetAsync($"{Probe}/crash");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        foreach (var leak in new[] { "boom", "hunter2", "db.internal", "InvalidOperationException", "   at ", "ContentReview.", "Exception" })
        {
            Assert.DoesNotContain(leak, raw);
        }

        var body = JsonDocument.Parse(raw).RootElement;
        Assert.False(body.TryGetProperty("detail", out _));
        Assert.False(body.TryGetProperty("exception", out _));
        Assert.False(body.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task The_500_is_just_as_generic_in_the_Development_environment()
    {
        // Development normally shows a developer exception page with the message and stack trace; the API must not.
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var baseFactory = new ApiFactory(connectionString, environment: "Development");
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ApiHost).Assembly)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Probe}/crash");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("boom", raw);
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("   at ", raw);
    }

    // ---- correlation id -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("abc-123")]
    [InlineData("A1B2C3")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    public async Task A_valid_client_correlation_id_is_echoed_on_success_and_on_errors(string id)
    {
        var ok = await GetAsync($"{Probe}/ok", id);
        var error = await GetAsync($"{Probe}/conflict", id);

        Assert.Equal(id, HeaderId(ok));
        Assert.Equal(id, HeaderId(error));
        Assert.Equal(id, (await BodyAsync(error)).GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("under_score")]
    [InlineData("café")]
    public async Task An_invalid_client_correlation_id_is_replaced_by_a_guid(string supplied)
    {
        var response = await GetAsync($"{Probe}/conflict", supplied);

        var id = HeaderId(response);
        Assert.NotEqual(supplied, id);
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(id, (await BodyAsync(response)).GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task The_correlation_id_is_limited_to_64_characters()
    {
        var sixtyFour = new string('a', 64);
        var sixtyFive = new string('a', 65);

        Assert.Equal(sixtyFour, HeaderId(await GetAsync($"{Probe}/ok", sixtyFour)));
        Assert.True(Guid.TryParse(HeaderId(await GetAsync($"{Probe}/ok", sixtyFive)), out _));
    }

    [Fact]
    public async Task Every_response_gets_a_new_id_when_the_client_sends_none()
    {
        var first = HeaderId(await GetAsync($"{Probe}/ok"));
        var second = HeaderId(await GetAsync($"{Probe}/ok"));

        Assert.NotEqual(first, second);
        Assert.True(Guid.TryParse(first, out _));
    }

    [Fact]
    public async Task Health_and_unknown_routes_also_carry_the_header()
    {
        Assert.NotEmpty(HeaderId(await GetAsync("/health")));
        Assert.NotEmpty(HeaderId(await GetAsync("/nope")));
    }

    // ---- logging --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Log_entries_of_a_request_carry_the_correlation_id_and_the_exception_stays_server_side()
    {
        await GetAsync($"{Probe}/crash", "trace-me-1");
        await GetAsync($"{Probe}/conflict", "trace-me-2");

        var crash = _api.Logs.Entries.Where(e => e.Scopes.Contains("CorrelationId=trace-me-1")).ToList();
        var error = Assert.Single(crash, e => e.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains("boom", error.Exception!.Message);

        var conflict = _api.Logs.Entries.Where(e => e.Scopes.Contains("CorrelationId=trace-me-2")).ToList();
        Assert.Contains(conflict, e => e.Message.Contains("409"));
        Assert.DoesNotContain(conflict, e => e.Scopes.Contains("CorrelationId=trace-me-1"));
    }

    [Fact]
    public async Task Passwords_tokens_and_authorization_headers_are_never_logged()
    {
        const string password = "Sup3r!Secret-Passw0rd";
        var token = await _api.LoginTokenAsync("author@demo.local", "Author");
        await _api.LoginAsync("author@demo.local", password, "Author"); // failed login with a distinctive password
        await _api.LoginAsync("author@demo.local", ApiHost.Password, "Author");
        await _api.SendAsync(HttpMethod.Get, "/api/auth/me", token);
        await _api.SendAsync(HttpMethod.Get, "/api/auth/me", "not-a-valid-token-value");
        await GetAsync($"{Probe}/crash");
        await _api.SendAsync(HttpMethod.Post, "/api/auth/logout", token);

        var everything = string.Join('\n', _api.Logs.Entries.Select(e => e.Everything));
        Assert.NotEmpty(_api.Logs.Entries);
        foreach (var secret in new[] { password, ApiHost.Password, token, "not-a-valid-token-value", "Bearer " })
        {
            Assert.DoesNotContain(secret, everything);
        }
    }

    // ---- model state and framework errors --------------------------------------------------------------------

    [Fact]
    public async Task Model_validation_uses_the_same_shape_with_camel_case_keys()
    {
        var response = await _api.Client.PostAsJsonAsync(
            $"{Probe}/model", new { fullName = "", count = 99, nested = new { name = (string?)null } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await BodyAsync(response);
        Assert.Equal("One or more validation errors occurred.", body.GetProperty("title").GetString());
        Assert.Equal(HeaderId(response), body.GetProperty("correlationId").GetString());
        var errors = body.GetProperty("errors");
        var keys = errors.EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["count", "fullName", "nested.name"], keys);
    }

    [Fact]
    public async Task A_request_body_that_is_not_valid_json_gets_a_400_problem()
    {
        var content = new StringContent("{ not json", Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

        var response = await _api.Client.PostAsync($"{Probe}/model", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HeaderId(response), (await BodyAsync(response)).GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Unknown_routes_and_wrong_methods_return_problem_details()
    {
        var token = await _api.LoginTokenAsync("author@demo.local", "Author");

        var notFound = await _api.SendAsync(HttpMethod.Get, "/api/does-not-exist", token);
        var wrongMethod = await _api.SendAsync(HttpMethod.Post, "/api/_probe/errors/ok", token);

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        foreach (var response in new[] { notFound, wrongMethod })
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(HeaderId(response), (await BodyAsync(response)).GetProperty("correlationId").GetString());
        }
    }

    [Fact]
    public async Task An_anonymous_request_to_an_unknown_route_gets_401_so_the_route_table_is_not_disclosed()
    {
        // The fallback policy (authentication required) also covers requests that match no endpoint.
        var response = await GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HeaderId(response), (await BodyAsync(response)).GetProperty("correlationId").GetString());
    }

    // ---- authentication and authorization responses ------------------------------------------------------------

    [Fact]
    public async Task Authentication_and_authorization_failures_carry_the_correlation_id()
    {
        var authorToken = await _api.LoginTokenAsync("author@demo.local", "Author");

        var unauthorized = await GetAsync("/api/auth/me", "auth-1");
        var forbidden = await _api.SendAsync(HttpMethod.Get, "/api/admin/_probe", authorToken);
        var badLogin = await _api.LoginAsync("author@demo.local", "Wrong!Passw0rd#1", "Author");

        foreach (var (response, status) in new[] { (unauthorized, 401), (forbidden, 403), (badLogin, 401) })
        {
            Assert.Equal(status, (int)response.StatusCode);
            var body = await BodyAsync(response);
            Assert.Equal(status, body.GetProperty("status").GetInt32());
            Assert.Equal(HeaderId(response), body.GetProperty("correlationId").GetString());
        }

        Assert.Equal("auth-1", HeaderId(unauthorized));
    }

    [Fact]
    public async Task The_login_rate_limit_response_carries_the_correlation_id()
    {
        await using var limited = await ApiHost.CreateAsync(postgres, loginAttemptsPerMinute: 1);
        await limited.LoginAsync("nobody@demo.local", "x", "Admin");

        var response = await limited.LoginAsync("nobody@demo.local", "x", "Admin");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(HeaderId(response), (await BodyAsync(response)).GetProperty("correlationId").GetString());
    }

    // ---- browser access ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Error_responses_keep_the_cors_headers_so_the_portals_can_read_them()
    {
        var response = await GetAsync($"{Probe}/crash", origin: "http://localhost:5173");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("X-Correlation-Id", string.Join(',', response.Headers.GetValues("Access-Control-Expose-Headers")));
    }
}
