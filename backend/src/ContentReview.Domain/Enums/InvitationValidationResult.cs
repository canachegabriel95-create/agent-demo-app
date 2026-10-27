namespace ContentReview.Domain.Enums;

/// <summary>Whether an invitation code may be used to register an email address, and if not, why.</summary>
public enum InvitationValidationResult
{
    Valid,
    NotFound,
    Expired,
    Revoked,
    AlreadyUsed,
    EmailMismatch,
}

public static class InvitationValidationResultExtensions
{
    /// <summary>The field of the registration form the reason belongs to.</summary>
    public static string Field(this InvitationValidationResult result) =>
        result == InvitationValidationResult.EmailMismatch ? "email" : "invitationCode";

    /// <summary>The sentence shown to the person who entered the code.</summary>
    public static string Message(this InvitationValidationResult result) => result switch
    {
        InvitationValidationResult.Valid => "The invitation code is valid.",
        InvitationValidationResult.NotFound => "This invitation code is not valid. Check the code or ask an administrator for a new one.",
        InvitationValidationResult.Expired => "This invitation has expired. Ask an administrator for a new one.",
        InvitationValidationResult.Revoked => "This invitation has been revoked. Ask an administrator for a new one.",
        InvitationValidationResult.AlreadyUsed => "This invitation has already been used.",
        InvitationValidationResult.EmailMismatch => "The email address does not match this invitation.",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };
}
