namespace ContentReview.Domain.Exceptions;

/// <summary>The caller is authenticated but may not perform this action. The API answers 403.</summary>
public class ForbiddenException(string? message = null)
    : Exception(message ?? "You do not have permission to perform this action.");
