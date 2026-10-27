using ContentReview.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ContentReview.Infrastructure.Auditing;

/// <summary>An attempt to change or delete an audit event.</summary>
public sealed class AuditImmutableException()
    : InvalidOperationException("Audit events are append-only: they cannot be modified or deleted.");

/// <summary>
/// Audit events are append-only. Saving a context in which one is modified or deleted fails before anything reaches the
/// database. (Raw SQL and <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> do not go through <c>SaveChanges</c>; the application
/// has none for audit events.)
/// </summary>
public sealed class AuditImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Check(DbContext? context)
    {
        if (context is not null
            && context.ChangeTracker.Entries<AuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new AuditImmutableException();
        }
    }
}
