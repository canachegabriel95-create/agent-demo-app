using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContentReview.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Reads <c>ConnectionStrings__Default</c> like the API does. When it is not set
/// (e.g. <c>migrations add</c>, which never connects) a credential-free placeholder is used so that no secret
/// ever lives in source. <c>database update</c> requires the real variable.
/// </summary>
internal sealed class ContentReviewDbContextFactory : IDesignTimeDbContextFactory<ContentReviewDbContext>
{
    private const string DesignTimeOnlyConnectionString = "Host=localhost;Database=content_review";

    public ContentReviewDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DesignTimeOnlyConnectionString;
        }

        var options = new DbContextOptionsBuilder<ContentReviewDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ContentReviewDbContext(options);
    }
}
