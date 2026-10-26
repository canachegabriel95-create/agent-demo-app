using ContentReview.IntegrationTests.Infrastructure;
using Npgsql;

namespace ContentReview.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class MigrateOnStartupTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_run_at_startup_when_the_flag_is_true()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(
            connectionString, new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "true" });

        using var client = factory.CreateClient();

        Assert.True(await MigrationHistoryExistsAsync(connectionString));
    }

    [Fact]
    public async Task Migrations_do_not_run_at_startup_when_the_flag_is_false()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(
            connectionString, new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });

        using var client = factory.CreateClient();

        Assert.False(await MigrationHistoryExistsAsync(connectionString));
    }

    [Fact]
    public async Task Migrations_do_not_run_by_default_outside_development()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString, environment: "Production");

        using var client = factory.CreateClient();

        Assert.False(await MigrationHistoryExistsAsync(connectionString));
    }

    [Fact]
    public async Task Migrations_run_by_default_in_development()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(connectionString, environment: "Development");

        using var client = factory.CreateClient();

        Assert.True(await MigrationHistoryExistsAsync(connectionString));
    }

    private static async Task<bool> MigrationHistoryExistsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
