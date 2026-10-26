namespace ContentReview.Api.Options;

/// <summary>Demo-data settings. Consumed by the demo seed and reset tooling (F15).</summary>
public class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }
}
