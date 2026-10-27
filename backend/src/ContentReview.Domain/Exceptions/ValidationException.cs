namespace ContentReview.Domain.Exceptions;

/// <summary>
/// One or more fields are invalid. The API answers 400 with an <c>errors</c> dictionary keyed by camelCase field name.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    /// <summary>Messages per field name; the API converts the names to camelCase.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
