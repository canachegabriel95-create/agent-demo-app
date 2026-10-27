namespace ContentReview.Domain.Exceptions;

/// <summary>
/// The resource does not exist or the caller may not know that it exists (for instance another author's article).
/// The API answers 404 without a detail, so both cases look the same.
/// </summary>
public class NotFoundException(string? message = null) : Exception(message ?? "Resource not found.");
