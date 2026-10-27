using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.UnitTests.Domain;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(2));

    private static User Create(
        string email = "Jane@Example.com",
        string passwordHash = "hash",
        string fullName = "Jane Doe",
        UserRole role = UserRole.Author,
        string? bio = null) =>
        User.Create(email, passwordHash, fullName, role, Now, bio);

    [Fact]
    public void Create_builds_an_active_user_with_normalized_email_and_utc_timestamps()
    {
        var user = Create(email: "  Jane@Example.COM ", fullName: "  Jane Doe  ", bio: "  Writes things.  ");

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(7, user.Id.Version);
        Assert.Equal("Jane@Example.COM", user.Email);
        Assert.Equal("jane@example.com", user.NormalizedEmail);
        Assert.Equal("Jane Doe", user.FullName);
        Assert.Equal("Writes things.", user.Bio);
        Assert.Equal(UserRole.Author, user.Role);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.SuspensionReason);
        Assert.Null(user.SuspendedAt);
        Assert.Equal(TimeSpan.Zero, user.CreatedAt.Offset);
        Assert.Equal(Now.UtcDateTime, user.CreatedAt.UtcDateTime);
        Assert.Equal(user.CreatedAt, user.UpdatedAt);
    }

    [Fact]
    public void Create_treats_a_blank_bio_as_missing()
    {
        Assert.Null(Create(bio: "   ").Bio);
        Assert.Null(Create(bio: null).Bio);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(100)]
    public void Create_accepts_names_on_the_length_boundaries(int length)
    {
        Assert.Equal(length, Create(fullName: new string('n', length)).FullName.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData(" a ")]
    public void Create_rejects_names_shorter_than_two_characters_after_trimming(string name)
    {
        Assert.Throws<ArgumentException>("fullName", () => Create(fullName: name));
    }

    [Fact]
    public void Create_rejects_names_longer_than_100_characters()
    {
        Assert.Throws<ArgumentException>("fullName", () => Create(fullName: new string('n', 101)));
    }

    [Fact]
    public void Create_accepts_a_1000_character_bio_and_rejects_1001()
    {
        Assert.Equal(1000, Create(bio: new string('b', 1000)).Bio!.Length);
        Assert.Throws<ArgumentException>("bio", () => Create(bio: new string('b', 1001)));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public void Create_rejects_invalid_emails(string email)
    {
        Assert.Throws<ArgumentException>("email", () => Create(email: email));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Create_rejects_undefined_roles(int role)
    {
        Assert.Throws<ArgumentOutOfRangeException>("role", () => Create(role: (UserRole)role));
    }

    [Fact]
    public void Create_requires_a_password_hash()
    {
        Assert.Throws<ArgumentException>("passwordHash", () => Create(passwordHash: " "));
    }

    [Fact]
    public void Create_generates_distinct_ids()
    {
        Assert.NotEqual(Create().Id, Create().Id);
    }

    private static User Author() =>
        User.Create("ann@example.com", "hash", "Ann Author", UserRole.Author, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), "Old bio");

    private static readonly DateTimeOffset Later = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UpdateProfile_changes_name_and_bio_and_reports_both()
    {
        var user = Author();

        var changed = user.UpdateProfile("  Ann B. Author ", "New bio", Later);

        Assert.Equal(["fullName", "bio"], changed);
        Assert.Equal("Ann B. Author", user.FullName);
        Assert.Equal("New bio", user.Bio);
        Assert.Equal(Later, user.UpdatedAt);
    }

    [Fact]
    public void UpdateProfile_reports_only_what_changed()
    {
        var onlyName = Author();
        var onlyBio = Author();

        Assert.Equal(["fullName"], onlyName.UpdateProfile("Ann Renamed", "Old bio", Later));
        Assert.Equal(["bio"], onlyBio.UpdateProfile("Ann Author", "Changed", Later));
    }

    [Fact]
    public void UpdateProfile_without_changes_leaves_the_user_untouched()
    {
        var user = Author();
        var before = user.UpdatedAt;

        var changed = user.UpdateProfile(" Ann Author ", " Old bio ", Later);

        Assert.Empty(changed);
        Assert.Equal(before, user.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateProfile_clears_the_bio_when_it_is_empty(string? bio)
    {
        var user = Author();

        var changed = user.UpdateProfile("Ann Author", bio, Later);

        Assert.Equal(["bio"], changed);
        Assert.Null(user.Bio);
    }

    [Fact]
    public void UpdateProfile_never_touches_email_role_status_or_password()
    {
        var user = Author();

        user.UpdateProfile("Someone Else", "Other", Later);

        Assert.Equal("ann@example.com", user.Email);
        Assert.Equal("ann@example.com", user.NormalizedEmail);
        Assert.Equal(UserRole.Author, user.Role);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal("hash", user.PasswordHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("  A  ")]
    public void UpdateProfile_rejects_an_invalid_name_and_changes_nothing(string name)
    {
        var user = Author();

        Assert.Throws<ArgumentException>(() => user.UpdateProfile(name, "x", Later));

        Assert.Equal("Ann Author", user.FullName);
        Assert.Equal("Old bio", user.Bio);
    }

    [Fact]
    public void UpdateProfile_enforces_the_length_limits()
    {
        var user = Author();

        user.UpdateProfile(new string('n', 100), new string('b', 1000), Later);

        Assert.Throws<ArgumentException>(() => user.UpdateProfile(new string('n', 101), null, Later));
        Assert.Throws<ArgumentException>(() => user.UpdateProfile("Ann", new string('b', 1001), Later));
    }

    // ---- suspension ----

    private const string Reason = "Posted spam links repeatedly.";

    [Fact]
    public void Suspend_stores_the_trimmed_reason_and_the_utc_time_and_changes_the_status()
    {
        var user = Author();

        user.Suspend("  " + Reason + "  ", Later);

        Assert.Equal(UserStatus.Suspended, user.Status);
        Assert.Equal(Reason, user.SuspensionReason);
        Assert.Equal(Later.ToUniversalTime(), user.SuspendedAt);
        Assert.Equal(TimeSpan.Zero, user.SuspendedAt!.Value.Offset);
        Assert.Equal(Later.ToUniversalTime(), user.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("         ")]
    [InlineData("too short")]
    public void Suspend_rejects_a_missing_or_short_reason_and_changes_nothing(string? reason)
    {
        var user = Author();

        Assert.Throws<ArgumentException>(() => user.Suspend(reason!, Later));

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.SuspensionReason);
        Assert.Null(user.SuspendedAt);
    }

    [Fact]
    public void Suspend_enforces_the_reason_limits_inclusively()
    {
        Author().Suspend(new string('r', 10), Later);
        Author().Suspend(new string('r', 500), Later);

        Assert.Throws<ArgumentException>(() => Author().Suspend(new string('r', 9), Later));
        Assert.Throws<ArgumentException>(() => Author().Suspend(new string('r', 501), Later));
    }

    [Fact]
    public void Suspending_twice_is_a_conflict_that_keeps_the_first_reason()
    {
        var user = Author();
        user.Suspend(Reason, Later);

        Assert.Throws<ConflictException>(() => user.Suspend("A different reason entirely.", Later.AddHours(1)));

        Assert.Equal(Reason, user.SuspensionReason);
        Assert.Equal(Later.ToUniversalTime(), user.SuspendedAt);
    }

    [Fact]
    public void An_administrator_cannot_be_suspended_or_reactivated()
    {
        var admin = Create(role: UserRole.Administrator);

        Assert.Throws<InvalidOperationException>(() => admin.Suspend(Reason, Later));
        Assert.Throws<InvalidOperationException>(() => admin.Reactivate(Later));
        Assert.Equal(UserStatus.Active, admin.Status);
    }

    [Fact]
    public void Reactivate_makes_the_author_active_and_clears_the_reason_and_the_time()
    {
        var user = Author();
        user.Suspend(Reason, Later);

        user.Reactivate(Later.AddDays(1));

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.SuspensionReason);
        Assert.Null(user.SuspendedAt);
        Assert.Equal(Later.AddDays(1).ToUniversalTime(), user.UpdatedAt);
    }

    [Fact]
    public void Reactivating_an_active_author_is_a_conflict()
    {
        var user = Author();

        Assert.Throws<ConflictException>(() => user.Reactivate(Later));

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void An_author_can_be_suspended_again_after_reactivation_with_a_new_reason()
    {
        var user = Author();
        user.Suspend(Reason, Later);
        user.Reactivate(Later.AddDays(1));

        user.Suspend("Second offence, harassment.", Later.AddDays(2));

        Assert.Equal("Second offence, harassment.", user.SuspensionReason);
        Assert.Equal(Later.AddDays(2).ToUniversalTime(), user.SuspendedAt);
    }

    [Fact]
    public void EnsureAuthorActive_passes_for_active_and_throws_forbidden_for_suspended_authors()
    {
        var user = Author();
        user.EnsureAuthorActive();

        user.Suspend(Reason, Later);

        Assert.Throws<ForbiddenException>(user.EnsureAuthorActive);

        user.Reactivate(Later.AddHours(1));
        user.EnsureAuthorActive();
    }
}
