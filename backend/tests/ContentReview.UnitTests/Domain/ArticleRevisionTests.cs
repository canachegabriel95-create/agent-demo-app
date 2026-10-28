using ContentReview.Domain.Articles;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.UnitTests.Domain;

public class ArticleRevisionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);
    private static readonly Guid Author = Guid.CreateVersion7();
    private static readonly Guid CategoryId = Guid.CreateVersion7();
    private static readonly Guid OtherCategoryId = Guid.CreateVersion7();
    private static readonly string Summary = new('s', 30);
    private static readonly string Body = new('b', 150);

    private static Article Published()
    {
        var article = Article.CreateDraft(Author, "A good title", Summary, Body, CategoryId, T0);
        article.Submit(T0);
        article.Approve(T0);
        return article;
    }

    private static ArticleRevision Draft() => ArticleRevision.CreateFrom(Published(), T1);

    private static ArticleRevision Pending()
    {
        var revision = Draft();
        revision.Submit(T1);
        return revision;
    }

    // ---- creating ----

    [Fact]
    public void A_revision_starts_as_a_draft_copy_of_the_published_content()
    {
        var article = Published();

        var revision = ArticleRevision.CreateFrom(article, T1);

        Assert.NotEqual(Guid.Empty, revision.Id);
        Assert.Equal(article.Id, revision.ArticleId);
        Assert.Equal(RevisionStatus.Draft, revision.Status);
        Assert.Equal(article.Title, revision.Title);
        Assert.Equal(article.Summary, revision.Summary);
        Assert.Equal(article.Body, revision.Body);
        Assert.Equal(article.CategoryId, revision.CategoryId);
        Assert.Equal(0, revision.SubmissionVersion);
        Assert.Null(revision.SubmittedAt);
        Assert.True(revision.IsOpen);
    }

    [Theory]
    [InlineData(ArticleStatus.Draft)]
    [InlineData(ArticleStatus.PendingReview)]
    [InlineData(ArticleStatus.Rejected)]
    public void Only_a_published_article_can_be_revised(ArticleStatus status)
    {
        var article = Article.CreateDraft(Author, "A good title", Summary, Body, CategoryId, T0);
        if (status != ArticleStatus.Draft)
        {
            article.Submit(T0);
        }

        if (status == ArticleStatus.Rejected)
        {
            article.Reject(T0);
        }

        Assert.Throws<InvalidTransitionException>(() => ArticleRevision.CreateFrom(article, T1));
    }

    [Fact]
    public void Creating_a_revision_leaves_the_published_article_untouched()
    {
        var article = Published();
        var version = article.PublishedVersion;

        _ = ArticleRevision.CreateFrom(article, T1);

        Assert.Equal(ArticleStatus.Published, article.Status);
        Assert.Equal("A good title", article.Title);
        Assert.Equal(version, article.PublishedVersion);
    }

    // ---- editing ----

    [Fact]
    public void A_draft_revision_can_be_edited_and_the_article_does_not_change()
    {
        var article = Published();
        var revision = ArticleRevision.CreateFrom(article, T1);

        revision.UpdateContent("  New title  ", "  New summary  ", "New body", OtherCategoryId, T2);

        Assert.Equal("New title", revision.Title);
        Assert.Equal("New summary", revision.Summary);
        Assert.Equal("New body", revision.Body);
        Assert.Equal(OtherCategoryId, revision.CategoryId);
        Assert.Equal(T2, revision.UpdatedAt);
        Assert.Equal("A good title", article.Title);
    }

    [Fact]
    public void Edits_use_the_draft_limits()
    {
        var revision = Draft();

        Assert.ThrowsAny<ArgumentException>(() => revision.UpdateContent(string.Empty, null, null, null, T2));
        Assert.ThrowsAny<ArgumentException>(() => revision.UpdateContent(new string('t', 151), null, null, null, T2));
        Assert.ThrowsAny<ArgumentException>(() => revision.UpdateContent("ok", new string('s', 501), null, null, T2));
        Assert.Equal("A good title", revision.Title);
    }

    [Fact]
    public void A_pending_revision_cannot_be_edited_or_deleted()
    {
        var revision = Pending();

        Assert.Throws<InvalidTransitionException>(() => revision.UpdateContent("Another title", Summary, Body, CategoryId, T2));
        Assert.Throws<InvalidTransitionException>(revision.EnsureCanDelete);
    }

    // ---- submitting ----

    [Fact]
    public void Submit_sends_the_revision_to_review_and_counts_the_submission()
    {
        var revision = Draft();

        revision.Submit(T2);

        Assert.Equal(RevisionStatus.PendingReview, revision.Status);
        Assert.Equal(1, revision.SubmissionVersion);
        Assert.Equal(T2, revision.SubmittedAt);
    }

    [Fact]
    public void Submit_applies_the_submission_rules_and_changes_nothing_when_they_fail()
    {
        var revision = Draft();
        revision.UpdateContent("abc", "short", "short", null, T2);

        var ex = Assert.Throws<ValidationException>(() => revision.Submit(T2));

        Assert.Contains("title", ex.Errors.Keys);
        Assert.Contains("summary", ex.Errors.Keys);
        Assert.Contains("body", ex.Errors.Keys);
        Assert.Contains("categoryId", ex.Errors.Keys);
        Assert.Equal(RevisionStatus.Draft, revision.Status);
        Assert.Equal(0, revision.SubmissionVersion);
    }

    [Fact]
    public void Withdraw_returns_a_pending_revision_to_draft()
    {
        var revision = Pending();

        revision.Withdraw(T2);

        Assert.Equal(RevisionStatus.Draft, revision.Status);
        Assert.Equal(1, revision.SubmissionVersion);
    }

    [Fact]
    public void A_rejected_revision_stays_editable_and_can_be_resubmitted()
    {
        var revision = Pending();
        revision.Reject(T2);
        Assert.Equal(RevisionStatus.Rejected, revision.Status);
        Assert.True(revision.IsOpen);

        revision.UpdateContent("A better title", Summary, Body, CategoryId, T2);
        revision.Submit(T2);

        Assert.Equal(RevisionStatus.PendingReview, revision.Status);
        Assert.Equal(2, revision.SubmissionVersion);
    }

    [Fact]
    public void Approve_makes_the_revision_final()
    {
        var revision = Pending();

        revision.Approve(T2);

        Assert.Equal(RevisionStatus.Approved, revision.Status);
        Assert.False(revision.IsOpen);
        Assert.Throws<InvalidTransitionException>(() => revision.Approve(T2));
        Assert.Throws<InvalidTransitionException>(() => revision.Reject(T2));
        Assert.Throws<InvalidTransitionException>(() => revision.Withdraw(T2));
    }

    [Fact]
    public void Only_a_pending_revision_can_be_decided_or_withdrawn()
    {
        var draft = Draft();

        Assert.Throws<InvalidTransitionException>(() => draft.Approve(T2));
        Assert.Throws<InvalidTransitionException>(() => draft.Reject(T2));
        Assert.Throws<InvalidTransitionException>(() => draft.Withdraw(T2));
    }

    // ---- replacing the published content ----

    [Fact]
    public void Replacing_the_content_publishes_the_revision_and_keeps_the_previous_version()
    {
        var article = Published();
        var revision = ArticleRevision.CreateFrom(article, T1);
        revision.UpdateContent("Revised title", "Revised summary of the article", new string('r', 200), OtherCategoryId, T1);
        revision.Submit(T1);
        revision.Approve(T2);

        var previous = article.ReplaceContentWith(revision, T2);

        Assert.Equal(ArticleStatus.Published, article.Status);
        Assert.Equal("Revised title", article.Title);
        Assert.Equal("Revised summary of the article", article.Summary);
        Assert.Equal(new string('r', 200), article.Body);
        Assert.Equal(OtherCategoryId, article.CategoryId);
        Assert.Equal(2, article.PublishedVersion);
        Assert.Equal(T2, article.CurrentVersionPublishedAt);
        Assert.Equal(T0, article.PublishedAt); // the first publication stays

        Assert.Equal(1, previous.VersionNumber);
        Assert.Equal("A good title", previous.Title);
        Assert.Equal(Summary, previous.Summary);
        Assert.Equal(Body, previous.Body);
        Assert.Equal(CategoryId, previous.CategoryId);
        Assert.Equal(T0, previous.PublishedAt);
        Assert.Equal(T2, previous.SupersededAt);
        Assert.Equal(revision.Id, previous.SupersededByRevisionId);
    }

    [Fact]
    public void A_second_replacement_keeps_both_earlier_versions_in_order()
    {
        var article = Published();
        var first = ArticleRevision.CreateFrom(article, T1);
        first.UpdateContent("Second title", Summary, Body, CategoryId, T1);
        first.Submit(T1);
        first.Approve(T1);
        var v1 = article.ReplaceContentWith(first, T1);

        var second = ArticleRevision.CreateFrom(article, T2);
        second.UpdateContent("Third title", Summary, Body, CategoryId, T2);
        second.Submit(T2);
        second.Approve(T2);
        var v2 = article.ReplaceContentWith(second, T2);

        Assert.Equal(3, article.PublishedVersion);
        Assert.Equal((1, "A good title", T0), (v1.VersionNumber, v1.Title, v1.PublishedAt));
        Assert.Equal((2, "Second title", T1), (v2.VersionNumber, v2.Title, v2.PublishedAt));
    }

    [Fact]
    public void Approving_an_article_the_first_time_starts_version_one()
    {
        var article = Published();

        Assert.Equal(1, article.PublishedVersion);
        Assert.Equal(T0, article.CurrentVersionPublishedAt);
    }

    [Fact]
    public void The_content_of_an_article_that_is_not_published_cannot_be_replaced()
    {
        var article = Article.CreateDraft(Author, "A good title", Summary, Body, CategoryId, T0);
        var revision = Pending();

        Assert.Throws<InvalidTransitionException>(() => article.ReplaceContentWith(revision, T2));
    }

    [Fact]
    public void AllowedActions_follow_the_revision_through_its_life()
    {
        var revision = Draft();
        Assert.Equal([ArticleAction.Edit, ArticleAction.Delete, ArticleAction.Submit], revision.AllowedActions(ActorKind.Owner));

        revision.Submit(T1);
        Assert.Equal([ArticleAction.Withdraw], revision.AllowedActions(ActorKind.Owner));
        Assert.Equal([ArticleAction.Approve, ArticleAction.Reject], revision.AllowedActions(ActorKind.Administrator));

        revision.Reject(T1);
        Assert.Equal([ArticleAction.Edit, ArticleAction.Delete, ArticleAction.Submit], revision.AllowedActions(ActorKind.Owner));
    }
}

public class RevisionReviewTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_rejection_needs_a_reason_of_10_to_1000_characters()
    {
        var revision = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();

        Assert.ThrowsAny<ArgumentException>(() => RevisionReview.Create(revision, 1, reviewer, ReviewDecision.Rejected, null, T0));
        Assert.ThrowsAny<ArgumentException>(() => RevisionReview.Create(revision, 1, reviewer, ReviewDecision.Rejected, "too short", T0));
        Assert.ThrowsAny<ArgumentException>(
            () => RevisionReview.Create(revision, 1, reviewer, ReviewDecision.Rejected, new string('x', 1001), T0));

        var review = RevisionReview.Create(revision, 2, reviewer, ReviewDecision.Rejected, "  Please add sources.  ", T0);
        Assert.Equal("Please add sources.", review.Reason);
        Assert.Equal(2, review.SubmissionVersion);
    }

    [Fact]
    public void An_approval_takes_no_reason_and_the_submission_starts_at_one()
    {
        var review = RevisionReview.Create(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), ReviewDecision.Approved, null, T0);

        Assert.Null(review.Reason);
        Assert.Equal(ReviewDecision.Approved, review.Decision);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RevisionReview.Create(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), ReviewDecision.Approved, null, T0));
    }
}
