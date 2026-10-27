using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ContentReview.IntegrationTests.Infrastructure;

/// <summary>One PostgreSQL container shared by all integration tests; each test gets its own database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates an empty database and returns a connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>
    /// Runs a SQL file with the <c>psql</c> client inside the container, as an operator would
    /// (<c>-v name=value</c> variables, <c>ON_ERROR_STOP</c>). The file is copied into the container first.
    /// </summary>
    public async Task<ExecResult> RunPsqlFileAsync(
        string connectionString, string hostFilePath, IReadOnlyDictionary<string, string> variables)
    {
        const string containerPath = "/tmp/script.sql";
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        await _container.CopyAsync(await File.ReadAllBytesAsync(hostFilePath), containerPath);

        var command = new List<string> { "psql", "-X", "-q", "-U", builder.Username!, "-d", builder.Database!, "-v", "ON_ERROR_STOP=1" };
        foreach (var (name, value) in variables)
        {
            command.Add("-v");
            command.Add($"{name}={value}");
        }

        command.Add("-f");
        command.Add(containerPath);

        return await _container.ExecAsync(command);
    }
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
