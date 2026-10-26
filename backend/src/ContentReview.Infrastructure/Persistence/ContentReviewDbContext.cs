using ContentReview.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Persistence;

/// <summary>EF Core context for the Content Review database. Mappings live in <c>Configurations/</c>.</summary>
public class ContentReviewDbContext(DbContextOptions<ContentReviewDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<ArticleSubmission> ArticleSubmissions => Set<ArticleSubmission>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContentReviewDbContext).Assembly);
    }
}
