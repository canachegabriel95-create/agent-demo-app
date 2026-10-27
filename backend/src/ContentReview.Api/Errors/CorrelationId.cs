namespace ContentReview.Api.Errors;

/// <summary>
/// The correlation id ties a response, its ProblemDetails and every log line of a request together.
/// A client may supply one; anything that is not a short, plain token is replaced so it cannot inject
/// characters into logs or headers.
/// </summary>
public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";
    public const int MaxLength = 64;

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>Returns <paramref name="supplied"/> when it is valid, otherwise a new GUID.</summary>
    public static string Resolve(string? supplied) => IsValid(supplied) ? supplied! : Guid.NewGuid().ToString();

    /// <summary>The id of the current request (falls back to the trace identifier outside the middleware).</summary>
    public static string From(HttpContext context) =>
        context.Items[ItemKey] as string ?? context.TraceIdentifier;
}

public interface ICorrelationIdAccessor
{
    string CorrelationId { get; }
}

public sealed class CorrelationIdAccessor(IHttpContextAccessor accessor) : ICorrelationIdAccessor, ContentReview.Infrastructure.Auditing.ICorrelationIdSource
{
    public string CorrelationId => accessor.HttpContext is { } context ? Errors.CorrelationId.From(context) : string.Empty;

    // Outside a request there is no id (an empty string would be recorded as one).
    string? ContentReview.Infrastructure.Auditing.ICorrelationIdSource.CorrelationId =>
        accessor.HttpContext is { } context ? Errors.CorrelationId.From(context) : null;
}
