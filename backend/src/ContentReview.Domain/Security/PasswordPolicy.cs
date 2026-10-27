namespace ContentReview.Domain.Security;

/// <summary>One rule of the password policy. The names are part of the shared test fixture and of the portal's mirror.</summary>
public enum PasswordRule
{
    TooShort,
    TooLong,
    MissingUppercase,
    MissingLowercase,
    MissingDigit,
    MissingSymbol,
}

/// <summary>
/// Password rules from the baseline: 12-128 characters with upper, lower, digit and symbol.
/// This is the only implementation on the server; the Author Portal mirrors it in <c>src/validation/passwordPolicy.ts</c>
/// and both are tested against <c>docs/fixtures/password-cases.json</c>.
/// </summary>
/// <remarks>
/// Characters are UTF-16 code units, as <see cref="string.Length"/> counts them. A symbol is any character that is
/// not a letter, a digit or white space.
/// </remarks>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    /// <summary>The sentences shown to the user, one per character requirement (length is a single sentence).</summary>
    public static IReadOnlyList<string> Rules { get; } =
    [
        $"Must be between {MinLength} and {MaxLength} characters long.",
        "Must contain at least one uppercase letter.",
        "Must contain at least one lowercase letter.",
        "Must contain at least one digit.",
        "Must contain at least one symbol (a character that is not a letter, a digit or white space).",
    ];

    /// <summary>The violated rules in declaration order; an empty list means the password is acceptable.</summary>
    public static IReadOnlyList<PasswordRule> Check(string? password)
    {
        var value = password ?? string.Empty;
        var violations = new List<PasswordRule>();

        if (value.Length < MinLength)
        {
            violations.Add(PasswordRule.TooShort);
        }

        if (value.Length > MaxLength)
        {
            violations.Add(PasswordRule.TooLong);
        }

        if (!value.Any(char.IsUpper))
        {
            violations.Add(PasswordRule.MissingUppercase);
        }

        if (!value.Any(char.IsLower))
        {
            violations.Add(PasswordRule.MissingLowercase);
        }

        if (!value.Any(char.IsDigit))
        {
            violations.Add(PasswordRule.MissingDigit);
        }

        if (!value.Any(IsSymbol))
        {
            violations.Add(PasswordRule.MissingSymbol);
        }

        return violations;
    }

    /// <summary>The sentence for a rule; both length rules share the length sentence.</summary>
    public static string Describe(PasswordRule rule) => rule switch
    {
        PasswordRule.TooShort or PasswordRule.TooLong => Rules[0],
        PasswordRule.MissingUppercase => Rules[1],
        PasswordRule.MissingLowercase => Rules[2],
        PasswordRule.MissingDigit => Rules[3],
        PasswordRule.MissingSymbol => Rules[4],
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
    };

    /// <summary>The sentences of the violated rules, each at most once; an empty list means the password is acceptable.</summary>
    public static IReadOnlyList<string> Validate(string? password) =>
        Check(password).Select(Describe).Distinct().ToList();

    public static bool IsValid(string? password) => Check(password).Count == 0;

    private static bool IsSymbol(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);
}
