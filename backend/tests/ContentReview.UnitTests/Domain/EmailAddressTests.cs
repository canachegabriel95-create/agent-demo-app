using ContentReview.Domain.Common;

namespace ContentReview.UnitTests.Domain;

public class EmailAddressTests
{
    [Theory]
    [InlineData("Jane.Doe@Example.COM", "jane.doe@example.com")]
    [InlineData("  author@example.com  ", "author@example.com")]
    [InlineData("MIXED@Case.Org", "mixed@case.org")]
    public void Normalize_trims_and_lowercases(string input, string expected)
    {
        Assert.Equal(expected, EmailAddress.Normalize(input));
    }

    [Theory]
    [InlineData("jane@example.com")]
    [InlineData("  jane@example.com ")]
    [InlineData("first.last+tag@sub.example.org")]
    public void IsValid_accepts_well_formed_addresses(string email)
    {
        Assert.True(EmailAddress.IsValid(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("two@@example.com")]
    [InlineData("missing-domain@")]
    [InlineData("@no-local.com")]
    [InlineData("no-dot@localhost")]
    [InlineData("Display Name <jane@example.com>")]
    public void IsValid_rejects_malformed_addresses(string? email)
    {
        Assert.False(EmailAddress.IsValid(email));
    }

    [Fact]
    public void IsValid_enforces_the_254_character_limit()
    {
        var atLimit = new string('a', 254 - "@example.com".Length) + "@example.com";
        var overLimit = new string('a', 255 - "@example.com".Length) + "@example.com";

        Assert.Equal(254, atLimit.Length);
        Assert.True(EmailAddress.IsValid(atLimit));
        Assert.False(EmailAddress.IsValid(overLimit));
    }
}
