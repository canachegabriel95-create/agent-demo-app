namespace ContentReview.Domain;

/// <summary>Marker used to reference the Domain assembly (tests, DI scanning).</summary>
public static class DomainAssembly
{
    public static readonly System.Reflection.Assembly Assembly = typeof(DomainAssembly).Assembly;
}
