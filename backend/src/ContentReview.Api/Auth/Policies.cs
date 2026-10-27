namespace ContentReview.Api.Auth;

/// <summary>Names of the authorization policies. Controllers reference these through the base controllers.</summary>
public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string AuthorOnly = "AuthorOnly";
}
