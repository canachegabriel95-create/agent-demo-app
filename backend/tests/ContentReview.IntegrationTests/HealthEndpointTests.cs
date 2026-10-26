using System.Net;
using System.Text.Json;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_returns_200_with_api_and_database_healthy_when_database_is_available()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());

        var checks = json.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["api"]);
        Assert.Equal("Healthy", checks["database"]);
    }

    [Fact]
    public async Task Health_returns_503_with_database_unhealthy_and_leaks_nothing_when_database_is_down()
    {
        // Nothing listens on port 1, so the connection is refused immediately.
        const string password = "s3cret-pw-do-not-leak";
        await using var factory = new ApiFactory(
            $"Host=localhost;Port=1;Database=content_review;Username=content_review;Password={password};Timeout=2");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var json = JsonDocument.Parse(body);
        var checks = json.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["api"]);
        Assert.Equal("Unhealthy", checks["database"]);

        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain("Host=", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task Host_fails_fast_with_a_clear_message_when_the_connection_string_is_missing()
    {
        await using var factory = new ApiFactory(connectionString: string.Empty);

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings__Default", error.ToString());
    }
}
