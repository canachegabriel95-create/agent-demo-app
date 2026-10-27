using ContentReview.Domain.Common;
using ContentReview.Domain.Enums;

namespace ContentReview.Infrastructure.Invitations;

public sealed record UserRef(Guid Id, string FullName);

/// <summary>An invitation with the display names of the people involved and its derived status.</summary>
public sealed record InvitationView(
    Guid Id,
    string Code,
    string Email,
    InvitationStatus Status,
    DateTimeOffset CreatedAt,
    UserRef CreatedBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    UserRef? RevokedBy,
    DateTimeOffset? UsedAt,
    UserRef? UsedBy,
    uint Version);

public sealed record InvitationFilter(string? Email, InvitationStatus? Status, int Page, int PageSize);

public interface IInvitationService
{
    /// <summary>Creates an Available invitation with a new unique code.</summary>
    /// <exception cref="Domain.Exceptions.ValidationException">The email or the validity is invalid.</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">A user with this email already exists.</exception>
    Task<InvitationView> CreateAsync(string? email, int? validForDays, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Newest first. The status filter is applied in SQL.</summary>
    Task<PagedResult<InvitationView>> ListAsync(InvitationFilter filter, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException"/>
    Task<InvitationView> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException"/>
    /// <exception cref="Domain.Exceptions.ConflictException">The version is stale or the invitation is not Available.</exception>
    Task<InvitationView> RevokeAsync(Guid id, uint version, Guid actorUserId, CancellationToken cancellationToken = default);
}
