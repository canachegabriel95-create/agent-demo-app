using ContentReview.Domain.Auditing;
using ContentReview.Domain.Common;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.Infrastructure.Invitations;

public sealed class InvitationService(
    ContentReviewDbContext db,
    IAuditWriter audit,
    TimeProvider timeProvider) : IInvitationService
{
    public const string UserExistsMessage = "A user with this email already exists.";
    public const string StaleVersionMessage = "The invitation was changed by someone else. Reload it and try again.";

    /// <summary>A collision of two random 60-bit codes is not realistic; the retry only guards the unique index.</summary>
    private const int MaxCodeAttempts = 5;

    public async Task<InvitationView> CreateAsync(
        string? email, int? validForDays, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (!EmailAddress.IsValid(email))
        {
            errors["email"] = [$"Enter a valid email address of at most {EmailAddress.MaxLength} characters."];
        }

        var days = validForDays ?? Invitation.DefaultValidityDays;
        if (days is < Invitation.MinValidityDays or > Invitation.MaxValidityDays)
        {
            errors["validForDays"] = [$"Validity must be between {Invitation.MinValidityDays} and {Invitation.MaxValidityDays} days."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var normalized = EmailAddress.Normalize(email!);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            throw new ConflictException(UserExistsMessage);
        }

        var now = timeProvider.GetUtcNow();
        for (var attempt = 1; ; attempt++)
        {
            var invitation = Invitation.Create(InvitationCode.Generate(), email!, actorUserId, now, days);
            db.Invitations.Add(invitation);
            audit.Record(
                AuditActions.InvitationCreated,
                nameof(Invitation),
                invitation.Id,
                actorUserId,
                new { email = invitation.Email, codeSuffix = InvitationCode.Suffix(invitation.Code) });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return await GetAsync(invitation.Id, cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt < MaxCodeAttempts && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Forget the rejected invitation (and its pending audit event) and draw another code.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<PagedResult<InvitationView>> ListAsync(InvitationFilter filter, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var invitations = db.Invitations.AsNoTracking();

        var term = filter.Email?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            invitations = invitations.Where(i => i.NormalizedEmail.Contains(term));
        }

        // The SQL form of Invitation.GetStatus: Used wins over Revoked, both win over Expired.
        invitations = filter.Status switch
        {
            InvitationStatus.Used => invitations.Where(i => i.UsedAt != null),
            InvitationStatus.Revoked => invitations.Where(i => i.UsedAt == null && i.RevokedAt != null),
            InvitationStatus.Expired => invitations.Where(i => i.UsedAt == null && i.RevokedAt == null && i.ExpiresAt <= now),
            InvitationStatus.Available => invitations.Where(i => i.UsedAt == null && i.RevokedAt == null && i.ExpiresAt > now),
            _ => invitations,
        };

        var totalCount = await invitations.CountAsync(cancellationToken);
        var rows = await Project(invitations
                .OrderByDescending(i => i.CreatedAt)
                .ThenByDescending(i => i.Id)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<InvitationView>(rows.Select(r => r.ToView(now)).ToList(), filter.Page, filter.PageSize, totalCount);
    }

    public async Task<InvitationView> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await Project(db.Invitations.AsNoTracking().Where(i => i.Id == id)).FirstOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException();
        return row.ToView(timeProvider.GetUtcNow());
    }

    public async Task<InvitationView> RevokeAsync(Guid id, uint version, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                         ?? throw new NotFoundException();
        if (invitation.Version != version)
        {
            throw new ConflictException(StaleVersionMessage);
        }

        invitation.Revoke(actorUserId, timeProvider.GetUtcNow());
        audit.Record(
            AuditActions.InvitationRevoked,
            nameof(Invitation),
            invitation.Id,
            actorUserId,
            new { email = invitation.Email, codeSuffix = InvitationCode.Suffix(invitation.Code) });

        // The UPDATE is conditional on the xmin that was read, so a racing change surfaces as a 409.
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private IQueryable<Row> Project(IQueryable<Invitation> invitations) =>
        invitations.Select(i => new Row(
            i,
            db.Users.Where(u => u.Id == i.CreatedByUserId).Select(u => u.FullName).First(),
            db.Users.Where(u => u.Id == i.RevokedByUserId).Select(u => u.FullName).FirstOrDefault(),
            db.Users.Where(u => u.Id == i.UsedByUserId).Select(u => u.FullName).FirstOrDefault()));

    private sealed record Row(Invitation Invitation, string CreatedByName, string? RevokedByName, string? UsedByName)
    {
        public InvitationView ToView(DateTimeOffset now) => new(
            Invitation.Id,
            Invitation.Code,
            Invitation.Email,
            Invitation.GetStatus(now),
            Invitation.CreatedAt,
            new UserRef(Invitation.CreatedByUserId, CreatedByName),
            Invitation.ExpiresAt,
            Invitation.RevokedAt,
            Invitation.RevokedByUserId is { } revokedBy ? new UserRef(revokedBy, RevokedByName ?? string.Empty) : null,
            Invitation.UsedAt,
            Invitation.UsedByUserId is { } usedBy ? new UserRef(usedBy, UsedByName ?? string.Empty) : null,
            Invitation.Version);
    }
}
