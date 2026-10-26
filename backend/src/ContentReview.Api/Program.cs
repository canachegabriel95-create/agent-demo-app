using System.Text.Json.Serialization;
using ContentReview.Api.Health;
using ContentReview.Api.Options;
using ContentReview.Infrastructure;
using ContentReview.Infrastructure.Persistence;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = true;
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddOptions<DemoOptions>().Bind(builder.Configuration.GetSection(DemoOptions.SectionName));
builder.Services.AddOptions<PortalCorsOptions>().Bind(builder.Configuration.GetSection(PortalCorsOptions.SectionName));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<PortalCorsOptions>>((cors, portals) =>
        cors.AddPolicy(PortalCorsOptions.PolicyName, policy => policy
            .WithOrigins(portals.Value.AllowedOrigins)
            .AllowAnyHeader()
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithExposedHeaders("X-Correlation-Id")));

builder.Services.AddInfrastructure();

builder.Services.AddHealthChecks()
    .AddCheck("api", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy())
    .AddDbContextCheck<ContentReviewDbContext>("database");

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)))
{
    throw new InvalidOperationException(
        "Connection string 'Default' is not configured. Set the ConnectionStrings__Default environment variable (see .env.example).");
}

// Migrations run at startup only when explicitly enabled (default: on in Development, off otherwise).
var migrateOnStartup = app.Configuration.GetValue<bool?>("Database:MigrateOnStartup") ?? app.Environment.IsDevelopment();
if (migrateOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ContentReviewDbContext>();
    await db.Database.MigrateAsync();
}

app.UseCors(PortalCorsOptions.PolicyName);

app.MapHealthChecks("/health", HealthResponseWriter.CreateOptions());
app.MapControllers();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
