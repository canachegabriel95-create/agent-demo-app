namespace ContentReview.Domain.Exceptions;

/// <summary>
/// The request conflicts with the current state of the resource (stale version, duplicate, already used...).
/// The API answers 409 and shows <see cref="Exception.Message"/>, so it must be a safe, user-facing sentence.
/// </summary>
public class ConflictException(string message) : Exception(message);
