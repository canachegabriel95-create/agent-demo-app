using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace ContentReview.Api.OpenApi;

/// <summary>Groups the operations in Swagger UI by area, in the order of the Admin and Author portals.</summary>
public static class ApiTags
{
    public const string Health = "Health";

    private static readonly Dictionary<string, string> ByController = new(StringComparer.Ordinal)
    {
        ["Auth"] = "Auth",
        ["Registration"] = "Registration",
        ["AdminInvitations"] = "Admin – Invitations",
        ["AdminUsers"] = "Admin – Users",
        ["AdminReviews"] = "Admin – Reviews",
        ["AdminRevisions"] = "Admin – Reviews",
        ["AdminDashboard"] = "Admin – Dashboard & Audit",
        ["AuditEvents"] = "Admin – Dashboard & Audit",
        ["Me"] = "Me",
        ["MyArticles"] = "Me",
        ["Articles"] = "Articles",
        ["ArticleRevisions"] = "Articles",
        ["Feed"] = "Feed",
        ["Categories"] = "Categories",
    };

    public static IReadOnlyCollection<string> All { get; } =
        [.. ByController.Values.Distinct(), Health];

    public static string For(ApiDescription api)
    {
        var controller = api.ActionDescriptor is ControllerActionDescriptor descriptor ? descriptor.ControllerName : null;
        return controller is not null && ByController.TryGetValue(controller, out var tag) ? tag : controller ?? "Other";
    }
}
