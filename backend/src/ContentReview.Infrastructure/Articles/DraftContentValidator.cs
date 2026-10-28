using ContentReview.Domain.Entities;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentReview.Infrastructure.Articles;

/// <summary>The cleaned content of a draft (an article or a revision) after the draft limits were applied.</summary>
internal sealed record DraftContent(string Title, string Summary, string Body, Guid? CategoryId);

/// <summary>The draft limits, shared by articles and their revisions so both report exactly the same errors.</summary>
internal static class DraftContentValidator
{
    /// <summary>Applies the draft limits and reports every broken one at once, keyed by field name.</summary>
    public static async Task<DraftContent> ValidateAsync(
        ContentReviewDbContext db,
        string? title,
        string? summary,
        string? body,
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        // NUL cannot be stored, so it is removed before anything is measured.
        var cleanTitle = (title ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        var cleanSummary = (summary ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        var cleanBody = (body ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal);

        var errors = new Dictionary<string, string[]>();
        if (cleanTitle.Length == 0)
        {
            errors["title"] = ["Title is required."];
        }
        else if (cleanTitle.Length > Article.TitleMaxLength)
        {
            errors["title"] = [$"Title must be at most {Article.TitleMaxLength} characters."];
        }

        if (cleanSummary.Length > Article.SummaryMaxLength)
        {
            errors["summary"] = [$"Summary must be at most {Article.SummaryMaxLength} characters."];
        }

        if (cleanBody.Length > Article.BodyMaxLength)
        {
            errors["body"] = [$"Body must be at most {Article.BodyMaxLength} characters."];
        }

        if (categoryId is { } category
            && (category == Guid.Empty || !await db.Categories.AnyAsync(c => c.Id == category, cancellationToken)))
        {
            errors["categoryId"] = ["Category does not exist."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new DraftContent(cleanTitle, cleanSummary, cleanBody, categoryId);
    }
}
