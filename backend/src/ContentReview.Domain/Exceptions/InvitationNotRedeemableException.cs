using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Exceptions;

/// <summary>
/// An invitation could not be consumed. <see cref="Reason"/> says why; the message is written for the person who
/// entered the code. The API answers 400.
/// </summary>
public class InvitationNotRedeemableException(InvitationValidationResult reason) : Exception(reason.Message())
{
    public InvitationValidationResult Reason { get; } = reason;
}
