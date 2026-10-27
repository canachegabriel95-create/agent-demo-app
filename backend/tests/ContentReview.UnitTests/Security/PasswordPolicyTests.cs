using ContentReview.Domain.Security;

namespace ContentReview.UnitTests.Security;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Str0ng!Passw0rd")]
    [InlineData("aA1!aaaaaaaa")]
    public void Accepts_passwords_that_meet_every_rule(string password)
    {
        Assert.True(PasswordPolicy.IsValid(password));
        Assert.Empty(PasswordPolicy.Validate(password));
    }

    [Fact]
    public void Accepts_a_password_of_exactly_the_maximum_length()
    {
        var password = "Aa1!" + new string('x', PasswordPolicy.MaxLength - 4);

        Assert.True(PasswordPolicy.IsValid(password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Aa1!aaaaaaa")]
    public void Rejects_passwords_that_are_too_short(string? password)
    {
        Assert.Contains(PasswordPolicy.Rules[0], PasswordPolicy.Validate(password));
    }

    [Fact]
    public void Rejects_passwords_that_are_too_long()
    {
        var password = "Aa1!" + new string('x', PasswordPolicy.MaxLength - 3);

        Assert.Contains(PasswordPolicy.Rules[0], PasswordPolicy.Validate(password));
    }

    [Theory]
    [InlineData("str0ng!passw0rd", 1)]
    [InlineData("STR0NG!PASSW0RD", 2)]
    [InlineData("Strong!Password", 3)]
    [InlineData("Str0ngPassw0rds", 4)]
    public void Reports_the_missing_character_class(string password, int ruleIndex)
    {
        var violations = PasswordPolicy.Validate(password);

        Assert.Equal([PasswordPolicy.Rules[ruleIndex]], violations);
    }

    [Fact]
    public void Reports_every_violated_rule()
    {
        Assert.Equal(PasswordPolicy.Rules.Count - 1, PasswordPolicy.Validate("short").Count);
    }
}
