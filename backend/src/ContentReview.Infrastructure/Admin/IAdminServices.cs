using ContentReview.Domain.Common;

namespace ContentReview.Infrastructure.Admin;

public interface IAdminDashboardService
{
    Task<AdminDashboard> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Read-only access to the audit history. Nothing here writes, changes or deletes an event.</summary>
public interface IAuditHistoryService
{
    /// <summary>Newest first. <c>From</c> is inclusive, <c>To</c> exclusive.</summary>
    Task<PagedResult<AuditEventItem>> ListAsync(AuditEventFilter filter, CancellationToken cancellationToken = default);
}
