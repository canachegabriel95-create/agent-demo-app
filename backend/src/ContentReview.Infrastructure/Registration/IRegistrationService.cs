namespace ContentReview.Infrastructure.Registration;

/// <summary>What the person typed into the registration form. Every value is checked by the service.</summary>
public sealed record RegistrationInput(
    string? InvitationCode,
    string? FullName,
    string? Email,
    string? Password,
    string? ConfirmPassword);

public sealed record RegisteredUser(Guid Id, string Email, string FullName);

public interface IRegistrationService
{
    /// <summary>
    /// Creates an Active Author account and uses up the invitation in one transaction. The role is never an input.
    /// Nothing is saved when any step fails, so the invitation stays Available.
    /// </summary>
    /// <exception cref="Domain.Exceptions.ValidationException">
    /// A field is invalid, or the invitation cannot be used (the error is keyed <c>invitationCode</c> or <c>email</c>).
    /// </exception>
    /// <exception cref="Domain.Exceptions.ConflictException">An account with this email already exists.</exception>
    Task<RegisteredUser> RegisterAsync(RegistrationInput input, CancellationToken cancellationToken = default);
}
