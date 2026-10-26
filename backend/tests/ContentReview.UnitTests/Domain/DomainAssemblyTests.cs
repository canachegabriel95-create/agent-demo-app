using ContentReview.Domain;
using ContentReview.Domain.Enums;

namespace ContentReview.UnitTests.Domain;

public class DomainAssemblyTests
{
    [Fact]
    public void Domain_assembly_has_no_dependencies_on_ef_core_or_aspnet_core()
    {
        var forbidden = DomainAssembly.Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                        || n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Enums_match_the_baseline_values()
    {
        Assert.Equal(["Administrator", "Author"], Enum.GetNames<UserRole>());
        Assert.Equal(["Active", "Suspended"], Enum.GetNames<UserStatus>());
        Assert.Equal(["Available", "Used", "Expired", "Revoked"], Enum.GetNames<InvitationStatus>());
        Assert.Equal(["Draft", "PendingReview", "Published", "Rejected"], Enum.GetNames<ArticleStatus>());
        Assert.Equal(["Approved", "Rejected"], Enum.GetNames<ReviewDecision>());
    }
}
