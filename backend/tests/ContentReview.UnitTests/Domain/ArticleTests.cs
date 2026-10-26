using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;

namespace ContentReview.UnitTests.Domain;

public class ArticleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Author = Guid.CreateVersion7();

    private static Article Draft(string title = "A title", string? summary = "", string? body = "", Guid? categoryId = null) =>
        Article.CreateDraft(Author, title, summary, body, categoryId, Now);

    [Fact]
    public void CreateDraft_starts_as_an_unsubmitted_draft()
    {
        var article = Draft("  My first article  ", "Short summary", "Some body text.");

        Assert.Equal(ArticleStatus.Draft, article.Status);
        Assert.Equal(0, article.SubmissionVersion);
        Assert.Null(article.SubmittedAt);
        Assert.Null(article.PublishedAt);
        Assert.Null(article.CategoryId);
        Assert.Equal(Author, article.AuthorId);
        Assert.Equal("My first article", article.Title);
        Assert.Equal("Short summary", article.Summary);
        Assert.Equal("Some body text.", article.Body);
        Assert.Equal(article.CreatedAt, article.UpdatedAt);
    }

    [Fact]
    public void CreateDraft_defaults_missing_summary_and_body_to_empty()
    {
        var article = Draft(summary: null, body: null);

        Assert.Equal(string.Empty, article.Summary);
        Assert.Equal(string.Empty, article.Body);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(150)]
    public void CreateDraft_accepts_titles_on_the_draft_boundaries(int length)
    {
        Assert.Equal(length, Draft(new string('t', length)).Title.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDraft_requires_a_title(string title)
    {
        Assert.Throws<ArgumentException>("title", () => Draft(title));
    }

    [Fact]
    public void CreateDraft_rejects_a_title_over_150_characters()
    {
        Assert.Throws<ArgumentException>("title", () => Draft(new string('t', 151)));
    }

    [Fact]
    public void CreateDraft_enforces_the_summary_limit_of_500()
    {
        Assert.Equal(500, Draft(summary: new string('s', 500)).Summary.Length);
        Assert.Throws<ArgumentException>("summary", () => Draft(summary: new string('s', 501)));
    }

    [Fact]
    public void CreateDraft_enforces_the_body_limit_of_20000()
    {
        Assert.Equal(20000, Draft(body: new string('b', 20000)).Body.Length);
        Assert.Throws<ArgumentException>("body", () => Draft(body: new string('b', 20001)));
    }

    [Fact]
    public void CreateDraft_keeps_the_body_exactly_as_written()
    {
        const string body = "  line one\n\n  line two  ";

        Assert.Equal(body, Draft(body: body).Body);
    }

    [Fact]
    public void CreateDraft_rejects_empty_identifiers()
    {
        Assert.Throws<ArgumentException>("authorId", () => Article.CreateDraft(Guid.Empty, "T", "", "", null, Now));
        Assert.Throws<ArgumentException>("categoryId", () => Draft(categoryId: Guid.Empty));
    }

    [Fact]
    public void CreateDraft_keeps_a_given_category()
    {
        var category = Guid.CreateVersion7();

        Assert.Equal(category, Draft(categoryId: category).CategoryId);
    }
}
