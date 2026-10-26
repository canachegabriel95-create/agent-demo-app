namespace ContentReview.Api.Options;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Lifetime of an opaque session token, in hours.</summary>
    public int SessionLifetimeHours { get; set; } = 8;
}
