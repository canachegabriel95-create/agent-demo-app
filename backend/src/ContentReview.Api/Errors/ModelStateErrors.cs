using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ContentReview.Api.Errors;

/// <summary>Turns model-state errors into the API's validation response: <c>errors</c> keyed by camelCase field.</summary>
public static class ModelStateErrors
{
    /// <summary>
    /// <c>Email</c> becomes <c>email</c>; <c>$.user.FullName</c> (JSON binding) becomes <c>user.fullName</c>;
    /// array indexes are kept: <c>Items[0].Name</c> becomes <c>items[0].name</c>.
    /// </summary>
    public static string ToCamelCaseKey(string key)
    {
        var trimmed = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key == "$" ? string.Empty : key;
        return string.Join('.', trimmed.Split('.').Select(CamelCase));

        static string CamelCase(string segment) =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..];
    }

    public static Dictionary<string, string[]> ToErrors(IReadOnlyDictionary<string, string[]> source)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, messages) in source)
        {
            var name = ToCamelCaseKey(key);
            errors[name] = errors.TryGetValue(name, out var existing) ? [.. existing, .. messages] : messages;
        }

        return errors;
    }

    public static IActionResult CreateResponse(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var details = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest);

        var source = details.Errors.ToDictionary(e => e.Key, e => e.Value);
        details.Errors.Clear();
        foreach (var (name, messages) in ToErrors(source))
        {
            details.Errors[name] = messages;
        }

        return new BadRequestObjectResult(details) { ContentTypes = { "application/problem+json" } };
    }
}
