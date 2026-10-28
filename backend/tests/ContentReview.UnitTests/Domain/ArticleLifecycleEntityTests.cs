using ContentReview.Domain.Articles;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.UnitTests.Domain;

public class ArticleLifecycleEntityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly Guid Author = Guid.CreateVersion7();
    private static readonly Guid CategoryId = Guid.CreateVersion7();

    private static readonly string Summary = new('s', 30);
    private static readonly string Body = new('b', 150);

    private static Article Complete() =>
        Article.CreateDraft(Author, "A good title", Summary, Body, CategoryId, T0);

    private static Article Pending()
    {
        var article = Complete();
        article.Submit(T0);
        return article;
    }

    private static Article InStatus(ArticleStatus status)
    {
        var article = Complete();
        switch (status)
        {
            case ArticleStatus.PendingReview:
                article.Submit(T0);
                break;
            case ArticleStatus.Published:
                article.Submit(T0);
                article.Approve(T0);
                break;
            case ArticleStatus.Rejected:
                article.Submit(T0);
                article.Reject(T0);
                break;
        }

        Assert.Equal(status, article.Status);
        return article;
    }

    // ---- submit ----

    [Fact]
    public void Submit_sends_a_complete_draft_to_review_and_counts_the_submission()
    {
        var article = Complete();

        article.Submit(T1);

        Assert.Equal(ArticleStatus.PendingReview, article.Status);
        Assert.Equal(1, article.SubmissionVersion);
        Assert.Equal(T1, article.SubmittedAt);
        Assert.Equal(T1, article.UpdatedAt);
        Assert.Null(article.PublishedAt);
    }

    [Fact]
    public void A_rejected_article_can_be_resubmitted_and_the_submission_number_goes_up()
    {
        var article = InStatus(ArticleStatus.Rejected);

        article.Submit(T1);

        Assert.Equal(ArticleStatus.PendingReview, article.Status);
        Assert.Equal(2, article.SubmissionVersion);
        Assert.Equal(T1, article.SubmittedAt);
    }

    [Fact]
    public void An_incomplete_draft_cannot_be_submitted_and_nothing_changes()
    {
        var article = Article.CreateDraft(Author, "Hi", "short", "tiny", null, T0);

        var failure = Assert.Throws<ValidationException>(() => article.Submit(T1));

        Assert.Equal(["body", "categoryId", "summary", "title"], failure.Errors.Keys.Order());
        Assert.Equal(ArticleStatus.Draft, article.Status);
        Assert.Equal(0, article.SubmissionVersion);
        Assert.Null(article.SubmittedAt);
        Assert.Equal(T0, article.UpdatedAt);
    }

    [Theory]
    [InlineData(ArticleStatus.PendingReview)]
    [InlineData(ArticleStatus.Published)]
    public void Submit_is_refused_when_the_article_is_already_with_the_reviewers_or_published(ArticleStatus status)
    {
        var article = InStatus(status);
        var version = article.SubmissionVersion;

        Assert.Throws<InvalidTransitionException>(() => article.Submit(T1));

        Assert.Equal(status, article.Status);
        Assert.Equal(version, article.SubmissionVersion);
    }

    [Fact]
    public void The_status_conflict_wins_over_field_errors()
    {
        var article = Article.CreateDraft(Author, "Hi", "", "", null, T0);
        // Force the article out of Draft without meeting the rules: Submit must report the status, not the fields.
        var published = InStatus(ArticleStatus.Published);

        Assert.Throws<InvalidTransitionException>(() => published.Submit(T1));
        Assert.Throws<ValidationException>(() => article.Submit(T1));
    }

    // ---- withdraw / approve / reject ----

    [Fact]
    public void Withdraw_returns_a_pending_article_to_draft_and_keeps_the_submission_count()
    {
        var article = Pending();

        article.Withdraw(T1);

        Assert.Equal(ArticleStatus.Draft, article.Status);
        Assert.Equal(1, article.SubmissionVersion);
        Assert.Equal(T1, article.UpdatedAt);
    }

    [Fact]
    public void Approve_publishes_and_stamps_the_publication_time()
    {
        var article = Pending();

        article.Approve(T1);

        Assert.Equal(ArticleStatus.Published, article.Status);
        Assert.Equal(T1, article.PublishedAt);
        Assert.Equal(T1, article.UpdatedAt);
    }

    [Fact]
    public void Reject_sends_the_article_back_to_its_author_without_a_publication_time()
    {
        var article = Pending();

        article.Reject(T1);

        Assert.Equal(ArticleStatus.Rejected, article.Status);
        Assert.Null(article.PublishedAt);
        Assert.Equal(T1, article.UpdatedAt);
    }

    [Theory]
    [InlineData(ArticleStatus.Draft)]
    [InlineData(ArticleStatus.Published)]
    [InlineData(ArticleStatus.Rejected)]
    public void Withdraw_approve_and_reject_need_a_pending_article(ArticleStatus status)
    {
        var article = InStatus(status);

        Assert.Throws<InvalidTransitionException>(() => article.Withdraw(T1));
        Assert.Throws<InvalidTransitionException>(() => article.Approve(T1));
        Assert.Throws<InvalidTransitionException>(() => article.Reject(T1));

        Assert.Equal(status, article.Status);
        Assert.Equal(T0, article.UpdatedAt);
    }

    [Fact]
    public void A_published_article_stays_published_whatever_is_attempted()
    {
        var article = InStatus(ArticleStatus.Published);

        Assert.Throws<InvalidTransitionException>(() => article.UpdateContent("New title", "s", "b", null, T1));
        Assert.Throws<InvalidTransitionException>(article.EnsureCanDelete);
        Assert.Throws<InvalidTransitionException>(() => article.Submit(T1));
        Assert.Throws<InvalidTransitionException>(() => article.Withdraw(T1));
        Assert.Throws<InvalidTransitionException>(() => article.Approve(T1));
        Assert.Throws<InvalidTransitionException>(() => article.Reject(T1));
        Assert.Equal(ArticleStatus.Published, article.Status);
        Assert.Equal("A good title", article.Title);
    }

    // ---- edit / delete ----

    [Theory]
    [InlineData(ArticleStatus.Draft)]
    [InlineData(ArticleStatus.Rejected)]
    public void UpdateContent_works_in_draft_and_rejected_and_keeps_the_status(ArticleStatus status)
    {
        var article = InStatus(status);
        var other = Guid.CreateVersion7();

        article.UpdateContent("  New title  ", "  New summary  ", "New body", other, T1);

        Assert.Equal(status, article.Status);
        Assert.Equal("New title", article.Title);
        Assert.Equal("New summary", article.Summary);
        Assert.Equal("New body", article.Body);
        Assert.Equal(other, article.CategoryId);
        Assert.Equal(T1, article.UpdatedAt);
    }

    [Fact]
    public void UpdateContent_is_refused_while_pending_review_and_changes_nothing()
    {
        var article = Pending();

        var failure = Assert.Throws<InvalidTransitionException>(() => article.UpdateContent("New title", "s", "b", null, T1));

        Assert.Equal("Article cannot be modified in status PendingReview.", failure.Message);
        Assert.Equal("A good title", article.Title);
        Assert.Equal(CategoryId, article.CategoryId);
        Assert.Equal(T0, article.UpdatedAt);
    }

    [Fact]
    public void UpdateContent_applies_the_draft_limits_and_changes_nothing_when_one_is_broken()
    {
        var article = Complete();

        Assert.Throws<ArgumentException>(() => article.UpdateContent("", "s", "b", null, T1));
        Assert.Throws<ArgumentException>(() => article.UpdateContent(new string('t', 151), "s", "b", null, T1));
        Assert.Throws<ArgumentException>(() => article.UpdateContent("ok", new string('s', 501), "b", null, T1));
        Assert.Throws<ArgumentException>(() => article.UpdateContent("ok", "s", new string('b', 20001), null, T1));
        Assert.Throws<ArgumentException>(() => article.UpdateContent("ok", "s", "b", Guid.Empty, T1));

        Assert.Equal("A good title", article.Title);
        Assert.Equal(Summary, article.Summary);
        Assert.Equal(Body, article.Body);
        Assert.Equal(CategoryId, article.CategoryId);
        Assert.Equal(T0, article.UpdatedAt);
    }

    [Fact]
    public void UpdateContent_accepts_the_draft_limits_exactly_and_may_clear_summary_body_and_category()
    {
        var article = Complete();

        article.UpdateContent(new string('t', 150), new string('s', 500), new string('b', 20000), null, T1);
        Assert.Equal(150, article.Title.Length);
        Assert.Equal(500, article.Summary.Length);
        Assert.Equal(20000, article.Body.Length);
        Assert.Null(article.CategoryId);

        article.UpdateContent("x", null, null, null, T1);
        Assert.Equal(string.Empty, article.Summary);
        Assert.Equal(string.Empty, article.Body);
    }

    [Fact]
    public void NUL_characters_are_removed_because_the_database_cannot_store_them()
    {
        var article = Article.CreateDraft(Author, "Ti\0tle", "sum\0mary", "bo\0dy", null, T0);

        Assert.Equal("Title", article.Title);
        Assert.Equal("summary", article.Summary);
        Assert.Equal("body", article.Body);

        article.UpdateContent("\0New\0", "\0", "a\0b", null, T1);

        Assert.Equal("New", article.Title);
        Assert.Equal(string.Empty, article.Summary);
        Assert.Equal("ab", article.Body);
    }

    [Theory]
    [InlineData(ArticleStatus.PendingReview)]
    [InlineData(ArticleStatus.Published)]
    [InlineData(ArticleStatus.Rejected)]
    public void Only_a_draft_can_be_deleted(ArticleStatus status)
    {
        Assert.Throws<InvalidTransitionException>(InStatus(status).EnsureCanDelete);
    }

    [Fact]
    public void A_draft_can_be_deleted()
    {
        Complete().EnsureCanDelete();
    }

    // ---- allowed actions ----

    [Fact]
    public void AllowedActions_follow_the_article_through_its_life()
    {
        var article = Complete();
        Assert.Equal([ArticleAction.Edit, ArticleAction.Delete, ArticleAction.Submit], article.AllowedActions(ActorKind.Owner));

        article.Submit(T1);
        Assert.Equal([ArticleAction.Withdraw], article.AllowedActions(ActorKind.Owner));
        Assert.Equal([ArticleAction.Approve, ArticleAction.Reject], article.AllowedActions(ActorKind.Administrator));

        article.Reject(T1);
        Assert.Equal([ArticleAction.Edit, ArticleAction.Submit], article.AllowedActions(ActorKind.Owner));

        article.Submit(T1);
        article.Approve(T1);
        Assert.Equal([ArticleAction.Revise], article.AllowedActions(ActorKind.Owner));
        Assert.Empty(article.AllowedActions(ActorKind.Administrator));
    }
}
