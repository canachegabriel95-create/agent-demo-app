using ContentReview.Domain.Auditing;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Users;

public sealed class UserManagementService(
    ContentReviewDbContext db,
    IUserDirectoryService directory,
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
