using ContentReview.Infrastructure.Sessions;

namespace ContentReview.UnitTests.Security;

public class AuthOptionsTests
{
    [Fact]
    public void Defaults_match_the_baseline()
    {
        var options = new AuthOptions();

        Assert.Equal(8, options.SessionLifetimeHours);
        Assert.Equal(10, options.LoginAttemptsPerMinute);
        Assert.Equal("Auth", AuthOptions.SectionName);
    }
}
