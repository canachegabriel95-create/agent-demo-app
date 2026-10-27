namespace ContentReview.Domain.Security;

/// <summary>Password rules from the baseline: 12-128 characters with upper, lower, digit and symbol.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static IReadOnlyList<string> Rules { get; } =
    [
        $"Must be between {MinLength} and {MaxLength} characters long.",
        "Must contain at least one uppercase letter.",
        "Must contain at least one lowercase letter.",
        "Must contain at least one digit.",
        "Must contain at least one symbol (a character that is not a letter or a digit).",
    ];

    /// <summary>Returns the violated rules; an empty list means the password is acceptable.</summary>
    public static IReadOnlyList<string> Validate(string? password)
    {
        var value = password ?? string.Empty;
        var violations = new List<string>();

        if (value.Length < MinLength || value.Length > MaxLength)
        {
            violations.Add(Rules[0]);
        }

        if (!value.Any(char.IsUpper))
        {
            violations.Add(Rules[1]);
        }

        if (!value.Any(char.IsLower))
        {
            violations.Add(Rules[2]);
        }

        if (!value.Any(char.IsDigit))
        {
            violations.Add(Rules[3]);
        }

        if (!value.Any(c => !char.IsLetterOrDigit(c)))
        {
            violations.Add(Rules[4]);
        }

        return violations;
    }

    public static bool IsValid(string? password) => Validate(password).Count == 0;
}
