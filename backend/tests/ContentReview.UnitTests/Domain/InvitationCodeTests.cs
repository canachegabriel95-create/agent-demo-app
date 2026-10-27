using ContentReview.Domain.Common;

namespace ContentReview.UnitTests.Domain;

public class InvitationCodeTests
{
    [Fact]
    public void Generated_codes_have_the_XXXX_XXXX_XXXX_shape_and_use_only_the_alphabet()
    {
        for (var i = 0; i < 500; i++)
        {
            var code = InvitationCode.Generate();

            Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", code);
            Assert.DoesNotContain(code, c => "ILOU".Contains(c));
        }
    }

    [Fact]
    public void The_alphabet_is_crockford_base32()
    {
        Assert.Equal(32, InvitationCode.Alphabet.Length);
        Assert.Equal(32, InvitationCode.Alphabet.Distinct().Count());
        Assert.All("ILOU", c => Assert.DoesNotContain(c, InvitationCode.Alphabet));
    }

    [Fact]
    public void Generated_codes_are_not_repeated()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => InvitationCode.Generate()).ToHashSet();

        Assert.Equal(2000, codes.Count);
    }

    [Fact]
    public void Every_alphabet_character_is_eventually_drawn()
    {
        var seen = Enumerable.Range(0, 3000).SelectMany(_ => InvitationCode.Generate()).Where(c => c != '-').ToHashSet();

        Assert.Equal(InvitationCode.Alphabet.ToHashSet(), seen);
    }

    [Theory]
    [InlineData("ABCD-EFGH-JKMN", "ABCD-EFGH-JKMN")]
    [InlineData("abcd-efgh-jkmn", "ABCD-EFGH-JKMN")]
    [InlineData("ABCDEFGHJKMN", "ABCD-EFGH-JKMN")]
    [InlineData("  abcdefghjkmn  ", "ABCD-EFGH-JKMN")]
    [InlineData("ab-cdef-ghjk-mn", "ABCD-EFGH-JKMN")]
    [InlineData("0123-4567-89AB", "0123-4567-89AB")]
    public void Normalize_accepts_any_case_with_or_without_dashes(string input, string expected) =>
        Assert.Equal(expected, InvitationCode.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCD-EFGH-JKM")]
    [InlineData("ABCD-EFGH-JKMNP")]
    [InlineData("ABCD-EFGH-JKMI")]
    [InlineData("ABCD-EFGH-JKML")]
    [InlineData("ABCD-EFGH-JKMO")]
    [InlineData("ABCD-EFGH-JKMU")]
    [InlineData("ABCD EFGH JKMN")]
    [InlineData("ABCD-EFGH-JKM!")]
    public void Normalize_rejects_anything_that_cannot_be_a_code(string? input) =>
        Assert.Null(InvitationCode.Normalize(input));

    [Fact]
    public void Normalizing_a_generated_code_returns_it_unchanged()
    {
        var code = InvitationCode.Generate();

        Assert.Equal(code, InvitationCode.Normalize(code));
        Assert.Equal(code, InvitationCode.Normalize(code.ToLowerInvariant()));
        Assert.Equal(code, InvitationCode.Normalize(code.Replace("-", string.Empty)));
    }

    [Fact]
    public void Suffix_is_only_the_last_four_characters()
    {
        Assert.Equal("JKMN", InvitationCode.Suffix("ABCD-EFGH-JKMN"));
        Assert.Equal("AB", InvitationCode.Suffix("AB"));
    }
}
