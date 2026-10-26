using ContentReview.Domain.Entities;

namespace ContentReview.UnitTests.Domain;

public class OtherEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Category_Create_accepts_a_valid_slug()
    {
        var category = Category.Create("  Local News ", "local-news", 3);

        Assert.Equal("Local News", category.Name);
        Assert.Equal("local-news", category.Slug);
        Assert.Equal(3, category.SortOrder);
    }

    [Theory]
    [InlineData("Local News")]
    [InlineData("UPPER")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("under_score")]
    [InlineData("")]
    public void Category_Create_rejects_invalid_slugs(string slug)
    {
        Assert.Throws<ArgumentException>("slug", () => Category.Create("Name", slug, 0));
    }

    [Fact]
    public void Category_Create_requires_a_name()
    {
        Assert.Throws<ArgumentException>("name", () => Category.Create(" ", "slug", 0));
    }

    [Fact]
    public void UserSession_Create_requires_an_expiry_after_creation()
    {
        var userId = Guid.CreateVersion7();

        var session = UserSession.Create(userId, "hash", Now, Now.AddHours(8));

        Assert.Equal(userId, session.UserId);
        Assert.Null(session.RevokedAt);
        Assert.Null(session.LastSeenAt);
        Assert.Throws<ArgumentException>("expiresAt", () => UserSession.Create(userId, "hash", Now, Now));
        Assert.Throws<ArgumentException>("expiresAt", () => UserSession.Create(userId, "hash", Now, Now.AddSeconds(-1)));
    }

    [Fact]
    public void UserSession_Create_requires_a_user_and_a_token_hash()
    {
        Assert.Throws<ArgumentException>("userId", () => UserSession.Create(Guid.Empty, "hash", Now, Now.AddHours(1)));
        Assert.Throws<ArgumentException>("tokenHash", () => UserSession.Create(Guid.CreateVersion7(), " ", Now, Now.AddHours(1)));
    }

    [Fact]
    public void ArticleSubmission_Create_snapshots_the_article_content()
    {
        var articleId = Guid.CreateVersion7();
        var categoryId = Guid.CreateVersion7();

        var submission = ArticleSubmission.Create(articleId, 2, "Title", "A summary", "A body", categoryId, Now);

        Assert.Equal(2, submission.Version);
        Assert.Equal(articleId, submission.ArticleId);
        Assert.Equal(categoryId, submission.CategoryId);
        Assert.Equal("A summary", submission.Summary);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ArticleSubmission_Create_rejects_versions_below_one(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>("version", () =>
            ArticleSubmission.Create(Guid.CreateVersion7(), version, "T", "S", "B", Guid.CreateVersion7(), Now));
    }

    [Fact]
    public void AuditEvent_Create_accepts_valid_json_details()
    {
        var actor = Guid.CreateVersion7();

        var audit = AuditEvent.Create(
            "invitation.created", "Invitation", Now, actor, Guid.CreateVersion7(), """{"codePrefix":"AB12"}""", "corr-1");

        Assert.Equal(actor, audit.ActorUserId);
        Assert.Equal("""{"codePrefix":"AB12"}""", audit.Details);
        Assert.Equal("corr-1", audit.CorrelationId);
    }

    [Fact]
    public void AuditEvent_Create_allows_system_events_without_an_actor_or_details()
    {
        var audit = AuditEvent.Create("demo.reset", "System", Now);

        Assert.Null(audit.ActorUserId);
        Assert.Null(audit.Details);
        Assert.Null(audit.CorrelationId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{broken")]
    public void AuditEvent_Create_rejects_invalid_json_details(string details)
    {
        Assert.Throws<ArgumentException>("detailsJson", () => AuditEvent.Create("a", "b", Now, detailsJson: details));
    }

    [Fact]
    public void AuditEvent_Create_requires_action_and_entity_type()
    {
        Assert.Throws<ArgumentException>("action", () => AuditEvent.Create(" ", "b", Now));
        Assert.Throws<ArgumentException>("entityType", () => AuditEvent.Create("a", "", Now));
    }
}
