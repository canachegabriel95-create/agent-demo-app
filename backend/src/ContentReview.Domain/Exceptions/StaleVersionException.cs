namespace ContentReview.Domain.Exceptions;

/// <summary>
/// The caller edited an old copy: the resource changed since it was read. The API answers 409 and includes
/// <see cref="CurrentVersion"/> (<c>currentVersion</c>) so the client can reload without guessing.
/// </summary>
public class StaleVersionException(string message, uint currentVersion) : ConflictException(message)
{
    public uint CurrentVersion { get; } = currentVersion;
}
