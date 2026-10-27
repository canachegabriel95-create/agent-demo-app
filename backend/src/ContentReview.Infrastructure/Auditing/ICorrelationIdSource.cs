namespace ContentReview.Infrastructure.Auditing;

/// <summary>The correlation id of the request being served, so an audit event can be traced to its request and logs.</summary>
public interface ICorrelationIdSource
{
    /// <summary>The id, or <c>null</c> outside a request (a command-line tool, a background job).</summary>
    string? CorrelationId { get; }
}

internal sealed class NoCorrelationIdSource : ICorrelationIdSource
{
    public string? CorrelationId => null;
}
