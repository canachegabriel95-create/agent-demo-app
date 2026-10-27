using ContentReview.Domain.Articles;

namespace ContentReview.UnitTests.Domain;

public class ArticleSubmissionValidatorTests
{
    private static readonly Guid Category = Guid.CreateVersion7();
    private static readonly string GoodSummary = new('s', 20);
    private static readonly string GoodBody = new('b', 100);

    private static IReadOnlyDictionary<string, string[]> Check(
        string? title = "Valid", string? summary = null, string? body = null, Guid? category = null) =>
        ArticleSubmissionValidator.Validate(title, summary ?? GoodSummary, body ?? GoodBody, category ?? Category);

    [Fact]
    public void An_article_on_the_minimum_of_every_rule_is_valid()
    {
        Assert.Empty(Check("12345", new string('s', 20), new string('b', 100)));
    }

    [Fact]
    public void An_article_on_the_maximum_of_every_rule_is_valid()
    {
        Assert.Empty(Check(new string('t', 150), new string('s', 500), new string('b', 20000)));
    }

    [Theory]
    [InlineData("", "Title is required.")]
    [InlineData("    ", "Title is required.")]
    [InlineData(null, "Title is required.")]
    [InlineData("1234", "Title must be between 5 and 150 characters.")]
    [InlineData("  123  ", "Title must be between 5 and 150 characters.")]
    public void Title_needs_5_characters_after_trimming(string? title, string message)
    {
        var errors = Check(title);

        Assert.Equal([message], errors["title"]);
        Assert.Single(errors);
    }

    [Fact]
    public void Title_over_150_characters_is_refused()
    {
        Assert.Equal(["Title must be between 5 and 150 characters."], Check(new string('t', 151))["title"]);
    }

    [Theory]
    [InlineData("", "Summary is required.")]
    [InlineData("   ", "Summary is required.")]
    [InlineData(null, "Summary is required.")]
    [InlineData("nineteen characters", "Summary must be between 20 and 500 characters.")]
    public void Summary_needs_20_characters_after_trimming(string? summary, string message)
    {
        var errors = ArticleSubmissionValidator.Validate("Valid", summary, GoodBody, Category);

        Assert.Equal([message], errors["summary"]);
        Assert.Single(errors);
    }

    [Fact]
    public void Summary_padded_with_spaces_does_not_count_and_501_characters_are_too_many()
    {
        Assert.True(Check(summary: new string('s', 19) + new string(' ', 10)).ContainsKey("summary"));
        Assert.True(Check(summary: new string('s', 501)).ContainsKey("summary"));
    }

    [Theory]
    [InlineData("", "Body is required.")]
    [InlineData("   \n  ", "Body is required.")]
    [InlineData(null, "Body is required.")]
    public void Body_is_required(string? body, string message)
    {
        var errors = ArticleSubmissionValidator.Validate("Valid", GoodSummary, body, Category);

        Assert.Equal([message], errors["body"]);
    }

    [Fact]
    public void Body_needs_100_characters_apart_from_padding_and_at_most_20000_in_total()
    {
        const string tooShort = "Body must be between 100 and 20000 characters.";

        Assert.Equal([tooShort], Check(body: new string('b', 99))["body"]);
        Assert.Equal([tooShort], Check(body: new string('b', 99) + new string(' ', 50))["body"]);
        Assert.Equal([tooShort], Check(body: new string('b', 20001))["body"]);
        Assert.Empty(Check(body: new string('b', 100)));
        Assert.Empty(Check(body: "  " + new string('b', 100) + "  "));
    }

    [Fact]
    public void A_category_is_required()
    {
        var none = ArticleSubmissionValidator.Validate("Valid", GoodSummary, GoodBody, null);
        var empty = ArticleSubmissionValidator.Validate("Valid", GoodSummary, GoodBody, Guid.Empty);

        Assert.Equal(["Category is required."], none["categoryId"]);
        Assert.Equal(["Category is required."], empty["categoryId"]);
    }

    [Fact]
    public void Every_broken_rule_is_reported_at_once_keyed_by_the_field_names_of_the_api()
    {
        var errors = ArticleSubmissionValidator.Validate("", "", "", null);

        Assert.Equal(["body", "categoryId", "summary", "title"], errors.Keys.Order());
    }
}
