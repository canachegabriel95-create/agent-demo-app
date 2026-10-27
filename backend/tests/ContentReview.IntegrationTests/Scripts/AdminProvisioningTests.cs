using System.Net;
using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Security;
using ContentReview.IntegrationTests.Infrastructure;

namespace ContentReview.IntegrationTests.Scripts;

/// <summary>End to end: an administrator created by db/scripts/provision-admin.sql can sign in to the Admin Portal only.</summary>
[Collection(PostgresCollection.Name)]
public class AdminProvisioningTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Email = "first.admin@demo.local";
    private const string Password = "Str0ng!Passw0rd#1";

    private static readonly string ScriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "provision-admin.sql");

    private readonly IPasswordHashingService _hashing = new PasswordHashingService();

    private ApiHost _api = null!;

    public async Task InitializeAsync()
    {
        _api = await ApiHost.CreateAsync(postgres);

        var result = await postgres.RunPsqlFileAsync(
            _api.ConnectionString,
            ScriptPath,
            new Dictionary<string, string>
            {
                ["admin_email"] = Email,
                ["admin_name"] = "First Admin",
                ["admin_password_hash"] = _hashing.Hash(Password),
            });
        Assert.Equal(0, result.ExitCode);
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    [Fact]
    public async Task The_provisioned_administrator_can_log_in_with_portal_Admin()
    {
        var response = await _api.LoginAsync(Email, Password, "Admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_provisioned_administrator_cannot_log_in_with_portal_Author()
    {
        var response = await _api.LoginAsync(Email, Password, "Author");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Running_the_script_again_does_not_change_the_password_that_works()
    {
        await postgres.RunPsqlFileAsync(
            _api.ConnectionString,
            ScriptPath,
            new Dictionary<string, string>
            {
                ["admin_email"] = Email,
                ["admin_name"] = "First Admin",
                ["admin_password_hash"] = _hashing.Hash("An0ther!Passw0rd#2"),
            });

        Assert.Equal(HttpStatusCode.OK, (await _api.LoginAsync(Email, Password, "Admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.LoginAsync(Email, "An0ther!Passw0rd#2", "Admin")).StatusCode);
    }
}
