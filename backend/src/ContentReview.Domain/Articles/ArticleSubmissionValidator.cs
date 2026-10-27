using ContentReview.Domain.Entities;

namespace ContentReview.Domain.Articles;

/// <summary>
/// The rules an article must meet to be submitted for review. A draft may be incomplete; a submission may not. Errors
/// are keyed by the field names the API uses (<c>title</c>, <c>summary</c>, <c>body</c>, <c>categoryId</c>).
/// </summary>
public static class ArticleSubmissionValidator
{
    public const int TitleMinLength = 5;
    public const int SummaryMinLength = 20;
    public const int BodyMinLength = 100;

    public static IReadOnlyDictionary<string, string[]> Validate(Article article) =>
        Validate(article.Title, article.Summary, article.Body, article.CategoryId);

    /// <summary>
    /// Lengths of the title and the summary are counted after trimming, and so is the minimum length of the body, so
    /// padding with spaces does not satisfy a minimum. The maximum length of the body counts every character.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Validate(string? title, string? summary, string? body, Guid? categoryId)
    {
        var errors = new Dictionary<string, string[]>();

        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length == 0)
        {
            errors["title"] = ["Title is required."];
        }
        else if (trimmedTitle.Length < TitleMinLength || trimmedTitle.Length > Article.TitleMaxLength)
        {
            errors["title"] = [$"Title must be between {TitleMinLength} and {Article.TitleMaxLength} characters."];
        }

        var trimmedSummary = summary?.Trim() ?? string.Empty;
        if (trimmedSummary.Length == 0)
        {
            errors["summary"] = ["Summary is required."];
        }
        else if (trimmedSummary.Length < SummaryMinLength || trimmedSummary.Length > Article.SummaryMaxLength)
        {
            errors["summary"] = [$"Summary must be between {SummaryMinLength} and {Article.SummaryMaxLength} characters."];
        }

        var text = body ?? string.Empty;
        if (text.Trim().Length == 0)
        {
            errors["body"] = ["Body is required."];
        }
        else if (text.Trim().Length < BodyMinLength || text.Length > Article.BodyMaxLength)
        {
            errors["body"] = [$"Body must be between {BodyMinLength} and {Article.BodyMaxLength} characters."];
        }

        if (categoryId is null || categoryId == Guid.Empty)
        {
            errors["categoryId"] = ["Category is required."];
        }

        return errors;
    }
}
