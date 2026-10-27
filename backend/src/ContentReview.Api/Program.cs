using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ContentReview.Api.Auth;
using ContentReview.Api.Controllers;
using ContentReview.Api.Health;
using ContentReview.Api.Options;
using ContentReview.Infrastructure;
using ContentReview.Infrastructure.Persistence;
using ContentReview.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authentication;
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
builder.Services.AddProblemDetails();

builder.Services.AddScoped<CredentialsValidator>();
builder.Services
    .AddAuthentication(SessionAuthentication.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthentication.Scheme, null);
builder.Services.AddAuthorization();

// Login attempts are limited per client IP; the limit is configurable (Auth:LoginAttemptsPerMinute, default 10).
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(AuthController.LoginRateLimitPolicy, httpContext =>
    {
        var attempts = httpContext.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.LoginAttemptsPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = attempts,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
    limiter.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Title = "Too many login attempts. Try again later." },
        });
    };
});

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
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health", HealthResponseWriter.CreateOptions());
app.MapControllers();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
