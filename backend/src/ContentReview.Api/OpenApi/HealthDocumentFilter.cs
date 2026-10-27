using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ContentReview.Api.OpenApi;

/// <summary>
/// <c>/health</c> is a mapped health check, not a controller action, so the API explorer does not list it. It is documented
/// here by hand because tools and the portals call it.
/// </summary>
public sealed class HealthDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        var report = context.SchemaGenerator.GenerateSchema(typeof(HealthReport), context.SchemaRepository);

        document.Paths ??= [];
        document.Paths["/health"] = new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = new()
                {
                    Summary = "Reports whether the API and its database are healthy.",
                    Description = "Anonymous. 200 when every check is Healthy, 503 when one is not; the body names the checks.",
                    Tags = new HashSet<OpenApiTagReference> { new(ApiTags.Health, document) },
                    Responses = new OpenApiResponses
                    {
                        ["200"] = new OpenApiResponse
                        {
                            Description = "Healthy.",
                            Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = report } },
                        },
                        ["503"] = new OpenApiResponse
                        {
                            Description = "At least one check is not healthy.",
                            Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = report } },
                        },
                    },
                },
            },
        };
    }

    /// <summary>The shape of the body written by <c>HealthResponseWriter</c>.</summary>
    public sealed record HealthReport(string Status, IReadOnlyList<HealthCheckEntry> Checks);

    public sealed record HealthCheckEntry(string Name, string Status);
}
