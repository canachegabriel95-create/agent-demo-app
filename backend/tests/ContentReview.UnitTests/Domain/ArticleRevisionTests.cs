using ContentReview.Domain.Articles;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.UnitTests.Domain;

public class ArticleRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(2);
    private static readonly Guid Author = Guid.CreateVersion7();
    private static readonly Guid Reviewer = Guid.CreateVersion7();
    private static readonly Guid Category = Guid.CreateVersion7();

    private const string Summary = "A summary that is long enough to submit.";
    private static readonly string Body = new('b', 120);

    private static Article PublishedArticle()
    {
        var article = Article.CreateDraft(Author, "Published title", Summary, Body, Category, Now);
        article.Submit(Now);
        article.Approve(Now);
        return article;
    }

    private static ArticleRevision NewRevision(Article? article = null) =>
        ArticleRevision.CreateFrom(article ?? PublishedArticle(), 1, Later);

    private static ArticleRevision Pending()
    {
        var revision = NewRevision();
        revision.Submit(Later);
        return revision;
    }

    [Fact]
    public void CreateFrom_copies_the_published_content_into_a_draft_revision()
    {
        var article = PublishedArticle();

        var revision = ArticleRevision.CreateFrom(article, 1, Later);

        Assert.NotEqual(Guid.Empty, revision.Id);
        Assert.Equal(article.Id, revision.ArticleId);
        Assert.Equal(1, revision.Number);
        Assert.Equal(RevisionStatus.Draft, revision.Status);
        Assert.Equal(article.Title, revision.Title);
        Assert.Equal(article.Summary, revision.Summary);
        Assert.Equal(article.Body, revision.Body);
        Assert.Equal(article.CategoryId, revision.CategoryId);
        Assert.Equal(0, revision.SubmissionVersion);
        Assert.Null(revision.SubmittedAt);
        Assert.Equal(Later, revision.CreatedAt);
    }

    [Theory]
    [InlineData(ArticleStatus.Draft)]
    [InlineData(ArticleStatus.PendingReview)]
    [InlineData(ArticleStatus.Rejected)]
    public void CreateFrom_requires_a_published_article(ArticleStatus status)
    {
        var article = Article.CreateDraft(Author, "A title", Summary, Body, Category, Now);
        if (status != ArticleStatus.Draft)
        {
            article.Submit(Now);
        }

        if (status == ArticleStatus.Rejected)
        {
            article.Reject(Now);
        }

        var ex = Assert.Throws<InvalidTransitionException>(() => ArticleRevision.CreateFrom(article, 1, Later));
        Assert.Equal($"Article cannot be revised in status {status}.", ex.Message);
    }

    [Fact]
    public void CreateFrom_numbers_start_at_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>("number", () => ArticleRevision.CreateFrom(PublishedArticle(), 0, Later));
    }

    [Fact]
    public void UpdateContent_replaces_the_content_with_the_draft_limits()
    {
        var revision = NewRevision();

        revision.UpdateContent("  New title  ", " new summary ", "new body", null, Later.AddHours(1));

        Assert.Equal("New title", revision.Title);
        Assert.Equal("new summary", revision.Summary);
        Assert.Equal("new body", revision.Body);
        Assert.Null(revision.CategoryId);
        Assert.Equal(Later.AddHours(1), revision.UpdatedAt);
    }

    [Fact]
    public void UpdateContent_applies_the_draft_limits()
    {
        var revision = NewRevision();

        Assert.Throws<ArgumentException>("title", () => revision.UpdateContent(" ", "s", "b", Category, Later));
        Assert.Throws<ArgumentException>("title", () => revision.UpdateContent(new string('t', 151), "s", "b", Category, Later));
        Assert.Throws<ArgumentException>("summary", () => revision.UpdateContent("title", new string('s', 501), "b", Category, Later));
        Assert.Throws<ArgumentException>("body", () => revision.UpdateContent("title", "s", new string('b', 20001), Category, Later));
        Assert.Throws<ArgumentException>("categoryId", () => revision.UpdateContent("title", "s", "b", Guid.Empty, Later));
    }

    [Fact]
    public void UpdateContent_is_refused_while_pending_review_and_after_approval()
    {
        var pending = Pending();
        Assert.Throws<InvalidTransitionException>(() => pending.UpdateContent("title", "s", "b", Category, Later));

        var article = PublishedArticle();
        var revision = ArticleRevision.CreateFrom(article, 1, Later);
        revision.Submit(Later);
        revision.Approve(Later);
        Assert.Throws<InvalidTransitionException>(() => revision.UpdateContent("title", "s", "b", Category, Later));
    }

    [Fact]
    public void Submit_counts_the_submission_and_records_when()
    {
        var revision = NewRevision();

        revision.Submit(Later.AddMinutes(5));

        Assert.Equal(RevisionStatus.PendingReview, revision.Status);
        Assert.Equal(1, revision.SubmissionVersion);
        Assert.Equal(Later.AddMinutes(5), revision.SubmittedAt);
    }

    [Fact]
    public void Submit_validates_like_an_article_and_changes_nothing_when_invalid()
    {
        var revision = NewRevision();
        revision.UpdateContent("abc", "short", "short", null, Later);

        var ex = Assert.Throws<ValidationException>(() => revision.Submit(Later));

        Assert.Equal(new[] { "body", "categoryId", "summary", "title" }, ex.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(RevisionStatus.Draft, revision.Status);
        Assert.Equal(0, revision.SubmissionVersion);
        Assert.Null(revision.SubmittedAt);
    }

    [Fact]
    public void Withdraw_returns_to_draft_and_a_resubmission_counts_again()
    {
        var revision = Pending();

        revision.Withdraw(Later.AddMinutes(10));

        Assert.Equal(RevisionStatus.Draft, revision.Status);
        Assert.Equal(Later.AddMinutes(10), revision.WithdrawnAt);

        revision.Submit(Later.AddMinutes(20));
        Assert.Equal(2, revision.SubmissionVersion);
        Assert.Null(revision.WithdrawnAt);
    }

    [Fact]
    public void Reject_leaves_the_revision_editable_and_resubmittable()
    {
        var revision = Pending();

        revision.Reject(Later.AddMinutes(30));

        Assert.Equal(RevisionStatus.Rejected, revision.Status);
        revision.UpdateContent("Reworked title", Summary, Body, Category, Later.AddMinutes(40));
        revision.Submit(Later.AddMinutes(50));
        Assert.Equal(RevisionStatus.PendingReview, revision.Status);
        Assert.Equal(2, revision.SubmissionVersion);
    }

    [Fact]
    public void Approve_closes_the_revision()
    {
        var revision = Pending();

        revision.Approve(Later.AddHours(3));

        Assert.Equal(RevisionStatus.Approved, revision.Status);
        Assert.Equal(Later.AddHours(3), revision.ApprovedAt);
    }

    [Fact]
    public void Transitions_that_are_not_allowed_throw_a_conflict_and_change_nothing()
    {
        var draft = NewRevision();
        Assert.Throws<InvalidTransitionException>(() => draft.Withdraw(Later));
        Assert.Throws<InvalidTransitionException>(() => draft.Approve(Later));
        Assert.Throws<InvalidTransitionException>(() => draft.Reject(Later));
        Assert.Equal(RevisionStatus.Draft, draft.Status);

        var pending = Pending();
        Assert.Throws<InvalidTransitionException>(() => pending.Submit(Later));
        Assert.Equal(RevisionStatus.PendingReview, pending.Status);
        Assert.Equal(1, pending.SubmissionVersion);

        pending.Approve(Later);
        Assert.Throws<InvalidTransitionException>(() => pending.Approve(Later));
        Assert.Throws<InvalidTransitionException>(() => pending.Reject(Later));
        Assert.Throws<InvalidTransitionException>(() => pending.Withdraw(Later));
    }

    [Fact]
    public void AllowedActions_follow_the_status()
    {
        var revision = NewRevision();
        Assert.Equal([RevisionAction.Edit, RevisionAction.Submit], revision.AllowedActions(ActorKind.Owner));
    }

    [Fact]
    public void ApplyRevision_replaces_the_published_content_and_hands_back_the_replaced_version()
    {
        var article = PublishedArticle();
        var originalTitle = article.Title;
        var revision = ArticleRevision.CreateFrom(article, 1, Later);
        revision.UpdateContent("Revised title", Summary + " Revised.", Body + " revised", Category, Later);
        revision.Submit(Later);
        revision.Approve(Later.AddHours(1));

        var replaced = article.ApplyRevision(revision, Later.AddHours(1));

        Assert.Equal("Revised title", article.Title);
        Assert.Equal(Summary + " Revised.", article.Summary);
        Assert.Equal(ArticleStatus.Published, article.Status);
        Assert.Equal(2, article.PublishedVersion);
        Assert.Equal(Later.AddHours(1), article.PublishedAt);
        Assert.Equal(originalTitle, replaced.Title);
        Assert.Equal(1, replaced.Version);
        Assert.Equal(article.Id, replaced.ArticleId);
        Assert.Equal(Now, replaced.PublishedAt);
        Assert.Equal(Later.AddHours(1), replaced.SupersededAt);
    }

    [Fact]
    public void ApplyRevision_requires_an_approved_revision_of_the_same_article()
    {
        var article = PublishedArticle();
        var pending = ArticleRevision.CreateFrom(article, 1, Later);
        pending.Submit(Later);
        Assert.Throws<ArgumentException>("revision", () => article.ApplyRevision(pending, Later));

        var other = PublishedArticle();
        var foreign = ArticleRevision.CreateFrom(other, 1, Later);
        foreign.Submit(Later);
        foreign.Approve(Later);
        Assert.Throws<ArgumentException>("revision", () => article.ApplyRevision(foreign, Later));
    }

    [Fact]
    public void The_first_approval_publishes_version_one()
    {
        var article = PublishedArticle();

        Assert.Equal(1, article.PublishedVersion);
    }

    [Fact]
    public void A_draft_article_has_no_published_version_and_cannot_be_revised()
    {
        var draft = Article.CreateDraft(Author, "A title", Summary, Body, Category, Now);

        Assert.Equal(0, draft.PublishedVersion);
        Assert.Throws<InvalidTransitionException>(draft.EnsureCanRevise);
    }
}
