namespace ContentReview.Api.Options;

public class PortalCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Portals";

    /// <summary>Origins of the Admin and Author portals. Defaults live in appsettings.json.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}
