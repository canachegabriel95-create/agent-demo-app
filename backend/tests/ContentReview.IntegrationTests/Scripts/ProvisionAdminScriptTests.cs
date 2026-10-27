using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Security;
using ContentReview.IntegrationTests.Infrastructure;
using Npgsql;

namespace ContentReview.IntegrationTests.Scripts;

/// <summary>Runs db/scripts/provision-admin.sql with psql against a migrated database, as an operator would.</summary>
[Collection(PostgresCollection.Name)]
public class ProvisionAdminScriptTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Email = "Admin@Demo.Local";
    private const string Name = "Platform Admin";
    private const string Password = "Str0ng!Passw0rd#1";

    private static readonly string ScriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "provision-admin.sql");

    private readonly IPasswordHashingService _hashing = new PasswordHashingService();

    private MigratedDatabase _database = null!;

    public async Task InitializeAsync() => _database = await MigratedDatabase.CreateAsync(postgres);

    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    private Task<DotNet.Testcontainers.Containers.ExecResult> RunAsync(string? email, string? name, string? hash)
    {
        var variables = new Dictionary<string, string>();
        if (email is not null)
        {
            variables["admin_email"] = email;
        }

        if (name is not null)
        {
            variables["admin_name"] = name;
        }

        if (hash is not null)
        {
            variables["admin_password_hash"] = hash;
        }

        return postgres.RunPsqlFileAsync(_database.ConnectionString, ScriptPath, variables);
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private Task<long> CountAsync(string table) => ScalarAsync<long>($"SELECT count(*) FROM {table}");

    [Fact]
    public async Task First_run_creates_one_active_administrator_with_the_supplied_hash()
    {
        var hash = _hashing.Hash(Password);

        var result = await RunAsync(Email, Name, hash);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, await CountAsync("users"));

        using var db = _database.NewContext();
        var admin = Assert.Single(db.Set<User>().ToList());
        Assert.Equal(UserRole.Administrator, admin.Role);
        Assert.Equal(UserStatus.Active, admin.Status);
        Assert.Equal(Email, admin.Email);
        Assert.Equal("admin@demo.local", admin.NormalizedEmail);
        Assert.Equal(Name, admin.FullName);
        Assert.Equal(hash, admin.PasswordHash);
        Assert.True(_hashing.Verify(admin.PasswordHash, Password));
    }

    [Fact]
    public async Task First_run_writes_an_audit_event_without_secrets()
    {
        var hash = _hashing.Hash(Password);

        await RunAsync(Email, Name, hash);

        Assert.Equal(1, await CountAsync("audit_events"));
        Assert.Equal("AdministratorProvisioned", await ScalarAsync<string>("SELECT action FROM audit_events"));
        Assert.Equal("User", await ScalarAsync<string>("SELECT entity_type FROM audit_events"));
        Assert.True(await ScalarAsync<bool>("SELECT actor_user_id IS NULL FROM audit_events"));
        Assert.True(await ScalarAsync<bool>("SELECT entity_id = (SELECT id FROM users) FROM audit_events"));

        var details = await ScalarAsync<string>("SELECT details::text FROM audit_events");
        Assert.Contains("admin@demo.local", details);
        Assert.DoesNotContain(hash, details);
        Assert.DoesNotContain(Password, details);
    }

    [Fact]
    public async Task Second_run_changes_nothing_and_keeps_the_original_password()
    {
        var originalHash = _hashing.Hash(Password);
        await RunAsync(Email, Name, originalHash);
        var xminBefore = await ScalarAsync<long>("SELECT xmin::text::bigint FROM users");

        var result = await RunAsync(
            Email.ToUpperInvariant(), "Someone Else", _hashing.Hash("An0ther!Passw0rd#2"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("already exists", result.Stderr + result.Stdout);
        Assert.Equal(1, await CountAsync("users"));
        Assert.Equal(1, await CountAsync("audit_events"));
        Assert.Equal(originalHash, await ScalarAsync<string>("SELECT password_hash FROM users"));
        Assert.Equal(Name, await ScalarAsync<string>("SELECT full_name FROM users"));
        Assert.Equal(xminBefore, await ScalarAsync<long>("SELECT xmin::text::bigint FROM users"));
    }

    [Fact]
    public async Task An_existing_non_administrator_with_the_same_email_is_left_untouched()
    {
        var author = User.Create("admin@demo.local", _hashing.Hash(Password), "Jane Author", UserRole.Author, DateTimeOffset.UtcNow);
        using (var db = _database.NewContext())
        {
            db.Add(author);
            await db.SaveChangesAsync();
        }

        var result = await RunAsync(Email, Name, _hashing.Hash("An0ther!Passw0rd#2"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("already exists with role Author", result.Stderr + result.Stdout);
        Assert.Equal(1, await CountAsync("users"));
        Assert.Equal(0, await CountAsync("audit_events"));
        Assert.Equal("Author", await ScalarAsync<string>("SELECT role FROM users"));
        Assert.Equal(author.PasswordHash, await ScalarAsync<string>("SELECT password_hash FROM users"));
    }

    public static TheoryData<string, string?, string?, string?> InvalidInputs()
    {
        var hash = new PasswordHashingService().Hash(Password);
        return new TheoryData<string, string?, string?, string?>
        {
            { "missing email", null, Name, hash },
            { "blank email", "   ", Name, hash },
            { "malformed email", "not-an-email", Name, hash },
            { "missing name", Email, null, hash },
            { "name too short", Email, "X", hash },
            { "name too long", Email, new string('x', 101), hash },
            { "missing hash", Email, Name, null },
            { "plain text instead of a hash", Email, Name, "Str0ng!Passw0rd#1" },
            { "hash without Identity V3 prefix", Email, Name, new string('A', 90) },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Missing_or_malformed_input_aborts_and_writes_nothing(
        string scenario, string? email, string? name, string? hash)
    {
        var result = await RunAsync(email, name, hash);

        Assert.True(result.ExitCode != 0, $"'{scenario}' should fail but exited with 0.");
        Assert.Equal(0, await CountAsync("users"));
        Assert.Equal(0, await CountAsync("audit_events"));
    }

    [Fact]
    public async Task Error_messages_do_not_echo_the_supplied_hash()
    {
        const string badHash = "plain-text-secret-value-that-must-not-leak-123456";

        var result = await RunAsync(Email, Name, badHash);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(badHash, result.Stderr + result.Stdout);
    }
}
