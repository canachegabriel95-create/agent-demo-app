using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests;

public class CorsTests
{
    // CORS headers do not depend on the database, so an unreachable one is fine here.
    private const string UnreachableDatabase = "Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://localhost:5174")]
    public async Task Portal_origins_are_allowed_and_expose_the_correlation_header(string origin)
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        var response = await client.SendAsync(request);

        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("X-Correlation-Id", response.Headers.GetValues("Access-Control-Expose-Headers").Single());
    }

    [Fact]
    public async Task Other_origins_are_not_allowed()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://evil.example");
        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_from_a_portal_allows_the_configured_methods_and_any_header()
    {
        await using var factory = new ApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "PATCH");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var response = await client.SendAsync(request);

        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var methods = response.Headers.GetValues("Access-Control-Allow-Methods").Single();
        foreach (var method in new[] { "GET", "POST", "PUT", "PATCH", "DELETE" })
        {
            Assert.Contains(method, methods);
        }
    }

    [Fact]
    public async Task Allowed_origins_can_be_overridden_by_configuration()
    {
        var settings = new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "http://localhost:9999" };
        await using var factory = new ApiFactory(UnreachableDatabase, settings);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://localhost:9999");
        var response = await client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
