using ContentReview.Domain;

namespace ContentReview.UnitTests;

public class SmokeTests
{
    [Fact]
    public void Domain_assembly_has_expected_name()
    {
        Assert.Equal("ContentReview.Domain", DomainAssembly.Assembly.GetName().Name);
    }

    [Fact]
    public void Domain_assembly_has_no_framework_dependencies_on_ef_or_aspnet()
    {
        var forbidden = DomainAssembly.Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                        || n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }
}
