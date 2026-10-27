using System.Data;
using ContentReview.Domain.Auditing;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using ContentReview.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Users;

public sealed class UserManagementService(
    ContentReviewDbContext db,
    IUserDirectoryService directory,
    ISessionService sessions,
    IAuditWriter audit,
    TimeProvider timeProvider) : IUserManagementService
{
    public const string StaleVersionMessage = "The profile was changed by someone else. Reload it and try again.";

    public async Task<UserDetails> UpdateProfileAsync(
        Guid id, string? fullName, string? bio, uint version, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Author, cancellationToken)
                   ?? throw new NotFoundException();

        ValidateProfile(fullName, bio);

        if (user.Version != version)
        {
            throw new ConflictException(StaleVersionMessage);
        }

        var changed = user.UpdateProfile(fullName!, bio, timeProvider.GetUtcNow());
        if (changed.Count > 0)
        {
            audit.Record(
                AuditActions.AdminProfileEdited,
                nameof(User),
                user.Id,
                actorUserId,
                new { targetUserId = user.Id, changedFields = changed });

            // The UPDATE is conditional on the xmin that was read, so a racing change surfaces as a 409.
            await db.SaveChangesAsync(cancellationToken);
        }

        return await directory.GetAsync(id, cancellationToken);
    }

    public Task<UserDetails> SuspendAsync(
        Guid id, string? reason, uint version, Guid actorUserId, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, version, "suspended", () => ValidateReason(reason), (user, text) =>
        {
            user.Suspend(text!, timeProvider.GetUtcNow());
            audit.Record(
                AuditActions.UserSuspended, nameof(User), user.Id, actorUserId, new { targetUserId = user.Id, reason = text });
        }, cancellationToken);

    public Task<UserDetails> ReactivateAsync(
        Guid id, uint version, Guid actorUserId, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, version, "reactivated", () => null, (user, _) =>
        {
            user.Reactivate(timeProvider.GetUtcNow());
            audit.Record(AuditActions.UserReactivated, nameof(User), user.Id, actorUserId, new { targetUserId = user.Id });
        }, cancellationToken);

    /// <summary>
    /// Loads the author, validates the input, checks the version, applies <paramref name="change"/> and saves it together with the revocation of all the
    /// author's sessions in one transaction. The UPDATE is conditional on the xmin that was read, so of two racing
    /// requests exactly one wins and the other gets a 409.
    /// </summary>
    private async Task<UserDetails> ChangeStatusAsync(
        Guid id,
        uint version,
        string verb,
        Func<string?> validate,
        Action<User, string?> change,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                   ?? throw new NotFoundException();

        if (user.Role != UserRole.Author)
        {
            throw new ValidationException("id", $"Administrator accounts cannot be {verb}.");
        }

        var input = validate();

        if (user.Version != version)
        {
            throw new ConflictException(StaleVersionMessage);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        change(user, input);
        await db.SaveChangesAsync(cancellationToken);
        await sessions.RevokeAllForUserAsync(user.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await directory.GetAsync(id, cancellationToken);
    }

    private static string ValidateReason(string? reason)
    {
        var text = reason?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new ValidationException("reason", "Reason is required.");
        }

        if (text.Length is < User.SuspensionReasonMinLength or > User.SuspensionReasonMaxLength)
        {
            throw new ValidationException(
                "reason",
                $"Reason must be between {User.SuspensionReasonMinLength} and {User.SuspensionReasonMaxLength} characters.");
        }

        return text;
    }

    private static void ValidateProfile(string? fullName, string? bio)
    {
        var errors = new Dictionary<string, string[]>();

        var name = fullName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors["fullName"] = ["Full name is required."];
        }
        else if (name.Length is < User.FullNameMinLength or > User.FullNameMaxLength)
        {
            errors["fullName"] = [$"Full name must be between {User.FullNameMinLength} and {User.FullNameMaxLength} characters."];
        }

        if ((bio?.Trim().Length ?? 0) > User.BioMaxLength)
        {
            errors["bio"] = [$"Biography must be at most {User.BioMaxLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
