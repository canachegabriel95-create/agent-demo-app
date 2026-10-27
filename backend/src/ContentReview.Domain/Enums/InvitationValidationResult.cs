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
    /// <summary>The sentence shown to the person who entered the code.</summary>
    public static string Message(this InvitationValidationResult result) => result switch
    {
        InvitationValidationResult.Valid => "The invitation code is valid.",
        InvitationValidationResult.NotFound => "This invitation code was not found.",
        InvitationValidationResult.Expired => "This invitation has expired.",
        InvitationValidationResult.Revoked => "This invitation has been revoked.",
        InvitationValidationResult.AlreadyUsed => "This invitation has already been used.",
        InvitationValidationResult.EmailMismatch => "This invitation was issued for a different email address.",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };
}
