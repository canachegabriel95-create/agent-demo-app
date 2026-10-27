using ContentReview.Domain.Auditing;
using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Auditing;
using ContentReview.Infrastructure.Articles;
using ContentReview.Infrastructure.Reviews;
using ContentReview.Infrastructure.Categories;
using ContentReview.Infrastructure.Invitations;
using ContentReview.Infrastructure.Registration;
using ContentReview.Infrastructure.Users;
using ContentReview.Infrastructure.Persistence;
using ContentReview.Infrastructure.Security;
using ContentReview.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentReview.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Registers the DbContext (Npgsql, snake_case) and the shared <see cref="IPasswordHashingService"/>. The connection string is resolved lazily from
    /// configuration (<c>ConnectionStrings__Default</c>) and is never stored in source.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ContentReviewDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>()
                .GetConnectionString(ConnectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. " +
                    "Set the ConnectionStrings__Default environment variable (see .env.example).");
            }

            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        });

        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHashingService, PasswordHashingService>();
        services.AddScoped<ISessionService, SessionService>();
        services.TryAddScoped<IAuditWriter, NoOpAuditWriter>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IInvitationRedemptionService, InvitationRedemptionService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IUserDirectoryService, UserDirectoryService>();
        services.AddScoped<IReviewQueueService, ReviewQueueService>();
        services.AddScoped<IReviewDecisionService, ReviewDecisionService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAuthorProfileService, AuthorProfileService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IArticleService, ArticleService>();
        services.AddScoped<IMyArticlesService, MyArticlesService>();

        return services;
    }
}
