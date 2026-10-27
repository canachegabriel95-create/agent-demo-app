using System.Security.Cryptography;

namespace ContentReview.Domain.Common;

/// <summary>
/// Invitation codes: 12 characters of the Crockford base32 alphabet (no I, L, O, U), shown as <c>XXXX-XXXX-XXXX</c>.
/// </summary>
public static class InvitationCode
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Length = 12;
    public const int GroupLength = 4;
    public const int SuffixLength = 4;

    /// <summary>A new random code from a cryptographic generator.</summary>
    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return Format(chars);
    }

    /// <summary>
    /// Canonical form of user input: trimmed, upper-case, dashes optional. <c>null</c> when the input cannot be a code
    /// (wrong length or a character outside the alphabet).
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        Span<char> chars = stackalloc char[Length];
        var count = 0;
        foreach (var c in input.Trim())
        {
            if (c == '-')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            if (count == Length || !Alphabet.Contains(upper))
            {
                return null;
            }

            chars[count++] = upper;
        }

        return count == Length ? Format(chars) : null;
    }

    /// <summary>The last characters of a code: all that audit events and logs may contain.</summary>
    public static string Suffix(string code) =>
        code.Length <= SuffixLength ? code : code[^SuffixLength..];

    private static string Format(ReadOnlySpan<char> chars) =>
        $"{chars[..GroupLength]}-{chars[GroupLength..(2 * GroupLength)]}-{chars[(2 * GroupLength)..]}";
}
