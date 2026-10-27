using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Persistence;
using ContentReview.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ContentReview.IntegrationTests.Infrastructure;

/// <summary>
/// The API running in-process against a fresh migrated database with a controllable clock, plus helpers to
/// seed users and call the auth endpoints.
/// </summary>
public sealed class ApiHost : IAsyncDisposable
{
    public const string Password = "Str0ng!Passw0rd#1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFactory _baseFactory;
    private readonly HttpClient _client;

    private ApiHost(
        ApiFactory baseFactory,
        WebApplicationFactoryHolder factory,
        string connectionString,
        FakeTimeProvider clock,
        CapturingLoggerProvider logs)
    {
        _baseFactory = baseFactory;
        Logs = logs;
        Factory = factory;
        ConnectionString = connectionString;
        Clock = clock;
        _client = factory.Inner.CreateClient();
    }

    public string ConnectionString { get; }
    public FakeTimeProvider Clock { get; }

    /// <summary>Everything the API logged, with scopes.</summary>
    public CapturingLoggerProvider Logs { get; }

    internal WebApplicationFactoryHolder Factory { get; }

    public HttpClient Client => _client;

    public IServiceProvider Services => Factory.Inner.Services;

    public static async Task<ApiHost> CreateAsync(PostgresFixture postgres, int loginAttemptsPerMinute = 1000)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
        var baseFactory = new ApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Database:MigrateOnStartup"] = "true",
                ["Auth:LoginAttemptsPerMinute"] = loginAttemptsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        var logs = new CapturingLoggerProvider();
        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<ILoggerProvider>(logs);
                services.AddControllers().AddApplicationPart(typeof(ApiHost).Assembly);
            });
        });

        return new ApiHost(baseFactory, new WebApplicationFactoryHolder(factory), connectionString, clock, logs);
    }

    public async Task<User> SeedUserAsync(
        string email, UserRole role, string password = Password, string fullName = "Test User", bool suspended = false)
    {
        var user = User.Create(email, new PasswordHashingService().Hash(password), fullName, role, Clock.GetUtcNow());
        using var scope = Factory.Inner.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentReviewDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (suspended)
        {
            await ExecuteAsync("UPDATE users SET status = 'Suspended' WHERE id = @id", ("id", user.Id));
        }

        return user;
    }

    public Task<HttpResponseMessage> LoginAsync(string? email, string? password, string? portal) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password, portal });

    public async Task<string> LoginTokenAsync(string email, string portal, string password = Password)
    {
        var response = await LoginAsync(email, password, portal);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString()!;
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, string? scheme = "Bearer")
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme!, token);
        }

        return _client.SendAsync(request);
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await Factory.Inner.DisposeAsync();
        await _baseFactory.DisposeAsync();

        // Every test uses its own database, hence its own Npgsql pool. Release the connections so a long test
        // run does not exhaust the server's max_connections.
        using var connection = new NpgsqlConnection(ConnectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    internal sealed record WebApplicationFactoryHolder(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Inner);
}
