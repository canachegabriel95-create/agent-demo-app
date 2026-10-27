namespace ContentReview.Infrastructure.Sessions;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Lifetime of an opaque session token, in hours.</summary>
    public int SessionLifetimeHours { get; set; } = 8;

    /// <summary>Login attempts allowed per client IP and minute before the API answers 429.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;
}
