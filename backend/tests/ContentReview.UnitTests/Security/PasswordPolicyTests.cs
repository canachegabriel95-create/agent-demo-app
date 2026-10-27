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

    [Theory]
    [MemberData(nameof(SharedCases))]
    public void Check_matches_the_shared_fixture(PasswordFixture.Case testCase)
    {
        var violations = PasswordPolicy.Check(testCase.Password).Select(rule => rule.ToString());

        Assert.Equal(testCase.Violations, violations);
        Assert.Equal(testCase.Violations.Length == 0, PasswordPolicy.IsValid(testCase.Password));
    }

    public static TheoryData<PasswordFixture.Case> SharedCases() => PasswordFixture.Cases();

    [Fact]
    public void The_fixture_covers_every_rule()
    {
        var covered = PasswordFixture.Load().SelectMany(c => c.Violations).ToHashSet();

        Assert.Equal(Enum.GetNames<PasswordRule>().ToHashSet(), covered);
    }

    [Fact]
    public void Rules_are_checked_in_declaration_order()
    {
        Assert.Equal(
            [PasswordRule.TooShort, PasswordRule.MissingUppercase, PasswordRule.MissingDigit, PasswordRule.MissingSymbol],
            PasswordPolicy.Check("short"));
    }

    [Theory]
    [InlineData(PasswordRule.TooShort, 0)]
    [InlineData(PasswordRule.TooLong, 0)]
    [InlineData(PasswordRule.MissingUppercase, 1)]
    [InlineData(PasswordRule.MissingLowercase, 2)]
    [InlineData(PasswordRule.MissingDigit, 3)]
    [InlineData(PasswordRule.MissingSymbol, 4)]
    public void Every_rule_has_its_sentence(PasswordRule rule, int index)
    {
        Assert.Equal(PasswordPolicy.Rules[index], PasswordPolicy.Describe(rule));
    }

    [Fact]
    public void Validate_never_repeats_a_sentence()
    {
        // A 200-character password of lower-case letters breaks TooLong only through the length sentence.
        var violations = PasswordPolicy.Validate(new string('a', 200));

        Assert.Equal(violations.Distinct().Count(), violations.Count);
        Assert.Contains(PasswordPolicy.Rules[0], violations);
    }

    [Fact]
    public void White_space_is_not_a_symbol()
    {
        Assert.Equal([PasswordRule.MissingSymbol], PasswordPolicy.Check("Str0ng Passw0rd"));
    }
}
