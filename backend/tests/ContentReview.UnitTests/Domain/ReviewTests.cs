using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;

namespace ContentReview.UnitTests.Domain;

public class ReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Article = Guid.CreateVersion7();
    private static readonly Guid Reviewer = Guid.CreateVersion7();

    private static Review Create(ReviewDecision decision, string? reason, int submissionVersion = 1) =>
        Review.Create(Article, submissionVersion, Reviewer, decision, reason, Now);

    [Fact]
    public void An_approval_needs_no_reason()
    {
        var review = Create(ReviewDecision.Approved, null);

        Assert.Equal(ReviewDecision.Approved, review.Decision);
        Assert.Null(review.Reason);
        Assert.Equal(1, review.SubmissionVersion);
        Assert.Equal(Reviewer, review.ReviewerId);
    }

    [Fact]
    public void A_rejection_stores_the_trimmed_reason()
    {
        var review = Create(ReviewDecision.Rejected, "  Needs more detail.  ");

        Assert.Equal("Needs more detail.", review.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("         ")]
    [InlineData("too short")]
    public void A_rejection_requires_a_reason_of_at_least_ten_characters(string? reason)
    {
        Assert.Throws<ArgumentException>("reason", () => Create(ReviewDecision.Rejected, reason));
    }

    [Fact]
    public void A_rejection_reason_is_limited_to_1000_characters()
    {
        Assert.Equal(10, Create(ReviewDecision.Rejected, new string('r', 10)).Reason!.Length);
        Assert.Equal(1000, Create(ReviewDecision.Rejected, new string('r', 1000)).Reason!.Length);
        Assert.Throws<ArgumentException>("reason", () => Create(ReviewDecision.Rejected, new string('r', 1001)));
    }

    [Fact]
    public void Create_rejects_an_undefined_decision()
    {
        Assert.Throws<ArgumentOutOfRangeException>("decision", () => Create((ReviewDecision)42, "A valid reason."));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_submission_version_below_one(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>("submissionVersion", () => Create(ReviewDecision.Approved, null, version));
    }

    [Fact]
    public void Create_rejects_empty_identifiers()
    {
        Assert.Throws<ArgumentException>("articleId", () => Review.Create(Guid.Empty, 1, Reviewer, ReviewDecision.Approved, null, Now));
        Assert.Throws<ArgumentException>("reviewerId", () => Review.Create(Article, 1, Guid.Empty, ReviewDecision.Approved, null, Now));
    }
}
