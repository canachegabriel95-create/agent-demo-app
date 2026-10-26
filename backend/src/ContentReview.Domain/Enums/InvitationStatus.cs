namespace ContentReview.Domain.Enums;

/// <summary>Derived from the invitation columns; never stored. See <c>Invitation.GetStatus</c>.</summary>
public enum InvitationStatus
{
    Available,
    Used,
    Expired,
    Revoked,
}
