using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Exceptions;

/// <summary>
/// A decision on an article or a revision could not be recorded because it is not as the administrator saw it
/// (changed, withdrawn, decided by someone else) or its author is suspended. The API answers 409 and includes
/// <see cref="CurrentStatus"/> (<c>currentStatus</c>, the name of an <see cref="ArticleStatus"/> or a
/// <see cref="RevisionStatus"/>) and <see cref="CurrentVersion"/> (<c>currentVersion</c>) so the portal can show what is
/// true now.
/// </summary>
public class ReviewConflictException(string message, string currentStatus, uint currentVersion) : ConflictException(message)
{
    public ReviewConflictException(string message, ArticleStatus currentStatus, uint currentVersion)
        : this(message, currentStatus.ToString(), currentVersion)
    {
    }

    public ReviewConflictException(string message, RevisionStatus currentStatus, uint currentVersion)
        : this(message, currentStatus.ToString(), currentVersion)
    {
    }

    public string CurrentStatus { get; } = currentStatus;
    public uint CurrentVersion { get; } = currentVersion;
}
