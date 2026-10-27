using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ContentReview.Api.OpenApi;

/// <summary>
/// ASP.NET lists every response as <c>text/plain</c>, <c>application/json</c> and <c>text/json</c>. The API answers
/// <c>application/json</c> on success and RFC 9457 <c>application/problem+json</c> on every error, and that is what the
/// document says.
/// </summary>
public sealed class ResponseContentTypesFilter : IOperationFilter
{
    private const string Json = "application/json";
    private const string Problem = "application/problem+json";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses is null)
        {
            return;
        }

        foreach (var (status, response) in operation.Responses)
        {
            if (response is not OpenApiResponse concrete || concrete.Content is not { Count: > 0 } content)
            {
                continue;
            }

            var isError = status.Length > 0 && status[0] is '4' or '5';
            var keep = isError ? Problem : Json;
            var schema = content.TryGetValue(keep, out var existing) ? existing.Schema
                : content.TryGetValue(Json, out var json) ? json.Schema
                : content.Values.First().Schema;

            concrete.Content = new Dictionary<string, OpenApiMediaType> { [keep] = new() { Schema = schema } };
        }
    }
}
