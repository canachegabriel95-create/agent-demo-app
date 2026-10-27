using System.Text.Json.Nodes;
using ContentReview.Api.Auth;
using ContentReview.Api.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ContentReview.Api.OpenApi;

/// <summary>
/// Examples shown in Swagger UI. They use demo data only: <c>*.demo.local</c> addresses, an example password and an example
/// invitation code that exists nowhere. Nothing here is, or looks like, a real credential.
/// </summary>
public sealed class DemoExamplesSchemaFilter : ISchemaFilter
{
    // A placeholder password; the demo accounts' passwords come from the environment, never from this file.
    public const string ExamplePassword = "Example-Passw0rd!";

    // A well-formed code that was never issued.
    public const string ExampleInvitationCode = "D3M0-0000-0001";

    private static readonly Dictionary<Type, Func<JsonNode>> Examples = new()
    {
        [typeof(LoginRequest)] = () => new JsonObject
        {
            ["email"] = "jane.author@demo.local",
            ["password"] = ExamplePassword,
            ["portal"] = "Author",
        },
        [typeof(ValidateInvitationRequest)] = () => new JsonObject { ["invitationCode"] = ExampleInvitationCode },
        [typeof(RegisterRequest)] = () => new JsonObject
        {
            ["invitationCode"] = ExampleInvitationCode,
            ["fullName"] = "Jane Author",
            ["email"] = "jane.author@demo.local",
            ["password"] = ExamplePassword,
            ["confirmPassword"] = ExamplePassword,
        },
        [typeof(CreateInvitationRequest)] = () => new JsonObject { ["email"] = "jane.author@demo.local", ["validForDays"] = 7 },
        [typeof(SuspendUserRequest)] = () => new JsonObject { ["reason"] = "Posting spam links in articles.", ["version"] = 1234 },
        [typeof(CreateArticleRequest)] = () => new JsonObject
        {
            ["title"] = "Why local content review matters",
            ["summary"] = "A short summary of at least twenty characters.",
            ["body"] = "The article text, at least one hundred characters when it is submitted for review.",
            ["categoryId"] = "00000000-0000-0000-0000-000000000000",
        },
    };

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is OpenApiSchema concrete && Examples.TryGetValue(context.Type, out var example))
        {
            concrete.Example = example();
        }
    }
}
