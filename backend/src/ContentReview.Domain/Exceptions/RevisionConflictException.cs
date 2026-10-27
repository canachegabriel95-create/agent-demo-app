using ContentReview.Domain.Enums;

namespace ContentReview.Domain.Exceptions;

/// <summary>
/// A decision on a revision could not be recorded because the revision is not as the administrator saw it (changed,
/// withdrawn, decided by someone else) or its author is suspended. The API answers 409 and includes
/// <see cref="CurrentStatus"/> (<c>currentStatus</c>) and <see cref="CurrentVersion"/> (<c>currentVersion</c>) so the
/// portal can show what is true now.
/// </summary>
public class RevisionConflictException(string message, RevisionStatus currentStatus, uint currentVersion) : ConflictException(message)
{
    public RevisionStatus CurrentStatus { get; } = currentStatus;
    public uint CurrentVersion { get; } = currentVersion;
}
