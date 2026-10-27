using System.Data;
using ContentReview.Domain.Auditing;
using ContentReview.Domain.Common;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Domain.Security;
using ContentReview.Infrastructure.Invitations;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.Infrastructure.Registration;

public sealed class RegistrationService(
    ContentReviewDbContext db,
    IInvitationRedemptionService redemption,
    IPasswordHashingService hashing,
    IAuditWriter audit,
    TimeProvider timeProvider) : IRegistrationService
{
    public const string EmailExistsMessage = "An account with this email already exists.";

    public async Task<RegisteredUser> RegisterAsync(RegistrationInput input, CancellationToken cancellationToken = default)
    {
        ValidateFields(input);

        var email = input.Email!.Trim();
        var normalizedEmail = EmailAddress.Normalize(email);

        // Say what is wrong with the code before anything about the email address: a caller without a valid invitation
        // for this address learns nothing about which accounts exist.
        var invitation = await redemption.ValidateAsync(input.InvitationCode, normalizedEmail, cancellationToken);
        ThrowIfNotValid(invitation);

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw await EmailTakenAsync(input, normalizedEmail, cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var user = User.Create(
                email, hashing.Hash(input.Password!), input.FullName!, UserRole.Author, timeProvider.GetUtcNow());
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            Guid invitationId;
            try
            {
                invitationId = await redemption.ConsumeAsync(input.InvitationCode, normalizedEmail, user.Id, cancellationToken);
            }
            catch (InvitationNotRedeemableException ex)
            {
                throw ToValidation(ex.Reason);
            }

            audit.Record(
                AuditActions.UserRegistered,
                nameof(User),
                user.Id,
                user.Id,
                new { userId = user.Id, email = user.Email, invitationId });
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new RegisteredUser(user.Id, user.Email, user.FullName);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw await EmailTakenAsync(input, normalizedEmail, cancellationToken);
        }
    }

    /// <summary>
    /// The address already has an account. If that account was just created with this very invitation (a concurrent
    /// registration won), the invitation is what the person needs to hear about; otherwise the address was taken.
    /// </summary>
    private async Task<Exception> EmailTakenAsync(RegistrationInput input, string normalizedEmail, CancellationToken cancellationToken)
    {
        var invitation = await redemption.ValidateAsync(input.InvitationCode, normalizedEmail, cancellationToken);
        return invitation == InvitationValidationResult.Valid ? new ConflictException(EmailExistsMessage) : ToValidation(invitation);
    }

    private static void ValidateFields(RegistrationInput input)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(input.InvitationCode))
        {
            errors["invitationCode"] = ["Invitation code is required."];
        }

        var fullName = input.FullName?.Trim() ?? string.Empty;
        if (fullName.Length == 0)
        {
            errors["fullName"] = ["Full name is required."];
        }
        else if (fullName.Length is < User.FullNameMinLength or > User.FullNameMaxLength)
        {
            errors["fullName"] = [$"Full name must be between {User.FullNameMinLength} and {User.FullNameMaxLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(input.Email))
        {
            errors["email"] = ["Email is required."];
        }
        else if (!EmailAddress.IsValid(input.Email))
        {
            errors["email"] = [$"Enter a valid email address of at most {EmailAddress.MaxLength} characters."];
        }

        if (string.IsNullOrEmpty(input.Password))
        {
            errors["password"] = ["Password is required."];
        }
        else
        {
            var violations = PasswordPolicy.Validate(input.Password);
            if (violations.Count > 0)
            {
                errors["password"] = [.. violations];
            }
        }

        if (string.IsNullOrEmpty(input.ConfirmPassword))
        {
            errors["confirmPassword"] = ["Confirm your password."];
        }
        else if (!string.IsNullOrEmpty(input.Password) && !string.Equals(input.Password, input.ConfirmPassword, StringComparison.Ordinal))
        {
            errors["confirmPassword"] = ["Passwords do not match."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static void ThrowIfNotValid(InvitationValidationResult result)
    {
        if (result != InvitationValidationResult.Valid)
        {
            throw ToValidation(result);
        }
    }

    private static ValidationException ToValidation(InvitationValidationResult reason) =>
        new(reason.Field(), reason.Message());
}
