namespace ContentReview.IntegrationTests;

public class SmokeTests
{
    [Fact]
    public void Api_assembly_is_referenced()
    {
        Assert.Equal("ContentReview.Api", typeof(Program).Assembly.GetName().Name);
    }
}
