using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Users;

public sealed class AuthorProfileService(ContentReviewDbContext db, TimeProvider timeProvider) : IAuthorProfileService
{
    public async Task<AuthorProfile> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ToProfile(await FindAsync(userId, cancellationToken));

    public async Task<AuthorProfile> UpdateAsync(
        Guid userId, string? fullName, string? bio, uint version, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(userId, cancellationToken);

        UserManagementService.ValidateProfile(fullName, bio);

        if (user.Version != version)
        {
            throw new ConflictException(UserManagementService.StaleVersionMessage);
        }

        if (user.UpdateProfile(fullName!, bio, timeProvider.GetUtcNow()).Count > 0)
        {
            // The UPDATE is conditional on the xmin that was read, so a racing change surfaces as a 409.
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToProfile(user);
    }

    private async Task<User> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.Author, cancellationToken)
        ?? throw new NotFoundException();

    private static AuthorProfile ToProfile(User user) =>
        new(user.Id, user.FullName, user.Email, user.Bio, user.Role, user.CreatedAt, user.Version);
}
