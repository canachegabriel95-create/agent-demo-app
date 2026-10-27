using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.UnitTests.Domain;

public class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    private static Invitation Create(int? validityDays = null, string email = "New.Author@Example.com") =>
        validityDays is null
            ? Invitation.Create("CODE-123", email, Admin, Now)
            : Invitation.Create("CODE-123", email, Admin, Now, validityDays.Value);

    private static void Set(Invitation invitation, string property, object value) =>
        typeof(Invitation).GetProperty(property)!.SetValue(invitation, value);

    [Fact]
    public void Create_defaults_to_seven_days_and_normalizes_the_email()
    {
        var invitation = Create();

        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);
        Assert.Equal("New.Author@Example.com", invitation.Email);
        Assert.Equal("new.author@example.com", invitation.NormalizedEmail);
        Assert.Equal(Admin, invitation.CreatedByUserId);
        Assert.Null(invitation.UsedAt);
        Assert.Null(invitation.RevokedAt);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void Create_accepts_validity_on_the_boundaries(int days)
    {
        Assert.Equal(Now.AddDays(days), Create(days).ExpiresAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(31)]
    public void Create_rejects_validity_outside_one_to_thirty_days(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>("validityDays", () => Create(days));
    }

    [Fact]
    public void Create_rejects_invalid_input()
    {
        Assert.Throws<ArgumentException>("email", () => Create(email: "nope"));
        Assert.Throws<ArgumentException>("code", () => Invitation.Create(" ", "a@b.co", Admin, Now));
        Assert.Throws<ArgumentException>("createdByUserId", () => Invitation.Create("C", "a@b.co", Guid.Empty, Now));
    }

    [Fact]
    public void Status_is_available_until_it_expires()
    {
        var invitation = Create();

        Assert.Equal(InvitationStatus.Available, invitation.GetStatus(Now));
        Assert.Equal(InvitationStatus.Available, invitation.GetStatus(invitation.ExpiresAt.AddTicks(-1)));
    }

    [Fact]
    public void Status_is_expired_exactly_at_the_expiry_instant_and_after()
    {
        var invitation = Create();

        Assert.Equal(InvitationStatus.Expired, invitation.GetStatus(invitation.ExpiresAt));
        Assert.Equal(InvitationStatus.Expired, invitation.GetStatus(invitation.ExpiresAt.AddDays(1)));
    }

    [Fact]
    public void Status_is_used_when_consumed_even_after_expiry()
    {
        var invitation = Create();
        Set(invitation, nameof(Invitation.UsedAt), Now.AddDays(1));

        Assert.Equal(InvitationStatus.Used, invitation.GetStatus(Now.AddDays(2)));
        Assert.Equal(InvitationStatus.Used, invitation.GetStatus(Now.AddDays(100)));
    }

    [Fact]
    public void Status_is_revoked_when_revoked_even_after_expiry()
    {
        var invitation = Create();
        Set(invitation, nameof(Invitation.RevokedAt), Now.AddDays(1));

        Assert.Equal(InvitationStatus.Revoked, invitation.GetStatus(Now.AddDays(2)));
        Assert.Equal(InvitationStatus.Revoked, invitation.GetStatus(Now.AddDays(100)));
    }

    [Fact]
    public void Status_prefers_used_over_revoked()
    {
        var invitation = Create();
        Set(invitation, nameof(Invitation.UsedAt), Now.AddDays(1));
        Set(invitation, nameof(Invitation.RevokedAt), Now.AddDays(2));

        Assert.Equal(InvitationStatus.Used, invitation.GetStatus(Now.AddDays(3)));
    }

    [Fact]
    public void Revoke_marks_an_available_invitation_as_revoked_by_the_administrator()
    {
        var invitation = Create();
        var other = Guid.CreateVersion7();

        invitation.Revoke(other, Now.AddHours(1));

        Assert.Equal(Now.AddHours(1), invitation.RevokedAt);
        Assert.Equal(other, invitation.RevokedByUserId);
        Assert.Equal(InvitationStatus.Revoked, invitation.GetStatus(Now.AddHours(2)));
    }

    [Fact]
    public void Revoke_is_rejected_when_the_invitation_is_expired()
    {
        var invitation = Create(validityDays: 1);

        var ex = Assert.Throws<InvalidTransitionException>(() => invitation.Revoke(Admin, Now.AddDays(1)));

        Assert.Contains("Expired", ex.Message);
        Assert.Null(invitation.RevokedAt);
    }

    [Fact]
    public void Revoke_is_rejected_when_the_invitation_is_used_or_already_revoked()
    {
        var used = Create();
        Set(used, nameof(Invitation.UsedAt), Now.AddHours(1));
        var revoked = Create();
        revoked.Revoke(Admin, Now.AddHours(1));

        Assert.Throws<InvalidTransitionException>(() => used.Revoke(Admin, Now.AddHours(2)));
        Assert.Throws<InvalidTransitionException>(() => revoked.Revoke(Admin, Now.AddHours(2)));
        Assert.Null(used.RevokedAt);
    }
}
