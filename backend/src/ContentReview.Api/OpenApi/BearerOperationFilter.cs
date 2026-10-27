using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ContentReview.Api.OpenApi;

/// <summary>
/// Every operation that is not <c>[AllowAnonymous]</c> needs the bearer token: the filter adds the security requirement and
/// documents the 401 (no or unusable token) and 403 (wrong role) responses, so no action has to repeat them.
/// </summary>
public sealed class BearerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor action || IsAnonymous(action))
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SwaggerSetup.BearerScheme, context.Document)] = [],
        });

        operation.Responses ??= [];
        AddProblem(operation, context, "401", "No token, or the token is invalid, expired or revoked, or the account is suspended.");
        AddProblem(operation, context, "403", "The signed-in user's role may not use this endpoint.");
    }

    private static bool IsAnonymous(ControllerActionDescriptor action) =>
        action.MethodInfo.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any()
        || action.ControllerTypeInfo.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any();

    private static void AddProblem(OpenApiOperation operation, OperationFilterContext context, string status, string description)
    {
        if (operation.Responses!.ContainsKey(status))
        {
            return;
        }

        var schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        operation.Responses[status] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType> { ["application/problem+json"] = new() { Schema = schema } },
        };
    }
}
