using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Exceptions;

/// <summary>
/// A decision on an article could not be recorded because the article is not as the administrator saw it (changed,
/// withdrawn, decided by someone else) or its author is suspended. The API answers 409 and includes
/// <see cref="CurrentStatus"/> (<c>currentStatus</c>) and <see cref="CurrentVersion"/> (<c>currentVersion</c>) so the
/// portal can show what is true now.
/// </summary>
public class ReviewConflictException(string message, ArticleStatus currentStatus, uint currentVersion) : ConflictException(message)
{
    public ArticleStatus CurrentStatus { get; } = currentStatus;
    public uint CurrentVersion { get; } = currentVersion;
}
