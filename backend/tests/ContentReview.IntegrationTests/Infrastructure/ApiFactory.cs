using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ContentReview.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API in-process with the given connection string and extra settings.
/// WebApplicationFactory defaults to Development, so the environment is made explicit (Production)
/// to keep migrate-on-startup off unless a test asks for it.
/// </summary>
public sealed class ApiFactory(
    string? connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    string environment = "Production")
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString };
            if (settings is not null)
            {
                foreach (var (key, value) in settings)
                {
                    values[key] = value;
                }
            }

            config.AddInMemoryCollection(values);
        });
    }
}
