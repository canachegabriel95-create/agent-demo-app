using ContentReview.Domain.Enums;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Admin;

public sealed class AdminDashboardService(ContentReviewDbContext db, TimeProvider timeProvider) : IAdminDashboardService
{
    public async Task<AdminDashboard> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // Authors only: administrators are not "users" here.
        var authors = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Author)
            .GroupBy(u => u.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // The Available rule of the invitation list: not used, not revoked, not expired.
        var available = await db.Invitations.AsNoTracking()
            .CountAsync(i => i.UsedAt == null && i.RevokedAt == null && i.ExpiresAt > now, cancellationToken);

        var articles = await db.Articles.AsNoTracking()
            .Where(a => a.Status == ArticleStatus.PendingReview || a.Status == ArticleStatus.Published)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        static int Of<T>(IEnumerable<(T Status, int Count)> rows, T status) where T : struct, Enum =>
            rows.FirstOrDefault(r => EqualityComparer<T>.Default.Equals(r.Status, status)).Count;

        var userRows = authors.Select(a => (a.Status, a.Count));
        var articleRows = articles.Select(a => (a.Status, a.Count));
        return new AdminDashboard(
            Of(userRows, UserStatus.Active),
            Of(userRows, UserStatus.Suspended),
            available,
            Of(articleRows, ArticleStatus.PendingReview),
            Of(articleRows, ArticleStatus.Published));
    }
}
