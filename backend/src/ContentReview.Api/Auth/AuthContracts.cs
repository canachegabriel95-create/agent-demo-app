using System.ComponentModel.DataAnnotations;
using ContentReview.Domain.Common;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Security;

namespace ContentReview.Api.Auth;

/// <summary>The portal a user signs in to. It must match the user's role.</summary>
public enum Portal
{
    Admin,
    Author,
}

public sealed record LoginRequest
{
    [Required, MaxLength(EmailAddress.MaxLength)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(PasswordPolicy.MaxLength)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public Portal? Portal { get; init; }
}

public sealed record UserSummary(Guid Id, string FullName, string Email, UserRole Role)
{
    public static UserSummary From(User user) => new(user.Id, user.FullName, user.Email, user.Role);
}

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserSummary User);

public sealed record CurrentUserResponse(Guid Id, string FullName, string Email, UserRole Role, UserStatus Status);

public static class PortalExtensions
{
    public static UserRole RequiredRole(this Portal portal) =>
        portal == Portal.Admin ? UserRole.Administrator : UserRole.Author;
}
