using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Enums;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Auth;

[Collection(PostgresCollection.Name)]
public class LoginRateLimitTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Login_is_limited_per_minute_and_over_the_limit_returns_a_429_problem()
    {
        await using var api = await ApiHost.CreateAsync(postgres, loginAttemptsPerMinute: 3);
        await api.SeedUserAsync("admin@demo.local", UserRole.Administrator);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var allowed = await api.LoginAsync("admin@demo.local", "Wrong!Passw0rd#1", "Admin");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var limited = await api.LoginAsync("admin@demo.local", ApiHost.Password, "Admin");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.Equal(429, (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
        Assert.True(limited.Headers.Contains("Retry-After"));
    }
}
