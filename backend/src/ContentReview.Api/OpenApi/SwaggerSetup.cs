using System.Reflection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ContentReview.Api.OpenApi;

/// <summary>Swagger UI and the OpenAPI document. Both exist only in Development and Demo (see <see cref="IsEnabled"/>).</summary>
public static class SwaggerSetup
{
    public const string DocumentName = "v1";
    public const string BearerScheme = "Bearer";

    /// <summary>The document and the UI are a developer tool: they are not served in Production.</summary>
    public static bool IsEnabled(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Demo");

    public static IServiceCollection AddContentReviewSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "Local Content Review Platform API",
                Version = DocumentName,
                Description =
                    "Admin Portal and Author Portal API. Sign in with `POST /api/auth/login`, copy the `token` from the response, " +
                    "click **Authorize** and paste it. Errors are RFC 9457 `application/problem+json` with a `correlationId`. " +
                    "Examples use demo data only.",
            });

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "Opaque session token",
                Description = "The `token` returned by `POST /api/auth/login` (no `Bearer ` prefix).",
            });

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml))
            {
                options.IncludeXmlComments(xml, includeControllerXmlComments: true);
            }

            options.TagActionsBy(api => [ApiTags.For(api)]);
            options.DocInclusionPredicate((_, _) => true);
            options.OrderActionsBy(api => $"{ApiTags.For(api)}|{api.RelativePath}|{api.HttpMethod}");
            options.UseInlineDefinitionsForEnums();
            options.OperationFilter<BearerOperationFilter>();
            options.OperationFilter<ResponseContentTypesFilter>();
            options.SchemaFilter<DemoExamplesSchemaFilter>();
            options.DocumentFilter<HealthDocumentFilter>();
        });

        return services;
    }

    public static WebApplication UseContentReviewSwagger(this WebApplication app)
    {
        if (!IsEnabled(app.Environment))
        {
            return app;
        }

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint($"/swagger/{DocumentName}/swagger.json", "Content Review API v1");
            options.DocumentTitle = "Content Review API";
            options.EnableTryItOutByDefault();
        });

        return app;
    }
}
