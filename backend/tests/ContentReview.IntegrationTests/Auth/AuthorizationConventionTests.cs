using System.Reflection;
using ContentReview.Api.Auth;
using ContentReview.Api.Controllers;
using ContentReview.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ContentReview.IntegrationTests.Auth;

/// <summary>
/// Guards the authorization conventions without a database: every API endpoint must say who may call it, and the
/// route prefixes must map to the matching policy. Adding an endpoint that breaks a rule fails these tests.
/// </summary>
public class AuthorizationConventionTests
{
    private static readonly Assembly ApiAssembly = typeof(AuthController).Assembly;

    private static IEnumerable<Type> ControllerTypes() =>
        ApiAssembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

    private static bool HasAccessRule(MemberInfo member) =>
        member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
        || member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    [Fact]
    public void There_are_controllers_to_check()
    {
        Assert.NotEmpty(ControllerTypes());
        Assert.All(ControllerTypes(), c => Assert.NotEmpty(Actions(c)));
    }

    [Fact]
    public void Every_controller_action_has_an_explicit_policy_or_AllowAnonymous()
    {
        var unprotected = ControllerTypes()
            .SelectMany(c => Actions(c).Select(a => (Controller: c, Action: a)))
            .Where(x => !HasAccessRule(x.Action) && !HasAccessRule(x.Controller))
            .Select(x => $"{x.Controller.Name}.{x.Action.Name}")
            .ToList();

        Assert.True(unprotected.Count == 0, $"Add [Authorize(Policy = ...)] or [AllowAnonymous] to: {string.Join(", ", unprotected)}");
    }

    [Fact]
    public void Routes_use_the_policy_that_matches_their_prefix()
    {
        var mismatches = new List<string>();
        foreach (var controller in ControllerTypes())
        {
            var route = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            var policies = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(a => a.Policy).ToList();

            string? required = route switch
            {
                _ when route.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase) => Policies.AdminOnly,
                _ when route.StartsWith("api/me", StringComparison.OrdinalIgnoreCase)
                       || route.StartsWith("api/articles", StringComparison.OrdinalIgnoreCase)
                       || route.StartsWith("api/feed", StringComparison.OrdinalIgnoreCase) => Policies.AuthorOnly,
                _ => null,
            };

            if (required is not null && !policies.Contains(required))
            {
                mismatches.Add($"{controller.Name} ({route}) must use {required}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    [Fact]
    public void The_base_controllers_carry_the_policies()
    {
        Assert.Equal(Policies.AdminOnly, typeof(AdminControllerBase).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(Policies.AuthorOnly, typeof(AuthorControllerBase).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Fact]
    public async Task Every_registered_endpoint_requires_authorization_except_the_documented_anonymous_ones()
    {
        // No database is touched: the host is only built to read its endpoint table.
        await using var factory = new ApiFactory("Host=localhost;Database=unused;Username=unused;Password=unused");
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);

        var anonymous = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => $"{string.Join("|", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])} /{e.RoutePattern.RawText?.TrimStart('/')}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        // The only anonymous endpoints. Registration and Swagger are added to this list by their own stories.
        Assert.Equal(["* /health", "POST /api/auth/login"], anonymous);

        var openWithoutRule = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        Assert.True(
            openWithoutRule.Count == 0,
            $"Endpoints without authorization metadata (they rely on the fallback policy): {string.Join(", ", openWithoutRule)}");
    }
}
