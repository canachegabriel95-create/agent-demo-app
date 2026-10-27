namespace ContentReview.Api.Errors;

/// <summary>
/// First middleware in the pipeline: resolves the correlation id, returns it in <c>X-Correlation-Id</c> on every
/// response (also error responses written later) and puts it in a logging scope so every log line of the request
/// carries it.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var id = CorrelationId.Resolve(context.Request.Headers[CorrelationId.HeaderName].ToString());
        context.Items[CorrelationId.ItemKey] = id;

        // OnStarting, not a plain assignment: the exception handler clears the response before it writes its own.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }
}
