using ContentReview.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ContentReview.IntegrationTests.Infrastructure;

/// <summary>A fresh database that the API has migrated at startup, plus helpers to talk to it.</summary>
public sealed class MigratedDatabase : IAsyncDisposable
{
    private readonly ApiFactory _factory;

    private MigratedDatabase(ApiFactory factory, string connectionString)
    {
        _factory = factory;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<MigratedDatabase> CreateAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var factory = new ApiFactory(
            connectionString, new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "true" });

        _ = factory.Services; // starts the host, which applies the migrations
        return new MigratedDatabase(factory, connectionString);
    }

    /// <summary>A new context (and scope) so tests can simulate independent requests.</summary>
    public ContentReviewDbContext NewContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<ContentReviewDbContext>();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
