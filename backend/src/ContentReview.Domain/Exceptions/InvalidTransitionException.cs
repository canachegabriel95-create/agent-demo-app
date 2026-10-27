namespace ContentReview.Domain.Exceptions;

/// <summary>A lifecycle action that is not allowed from the current state (for example approving a Draft). 409.</summary>
public class InvalidTransitionException(string message) : ConflictException(message);
