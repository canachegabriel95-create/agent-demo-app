using System.Net.Mail;

namespace ContentReview.Domain.Common;

public static class EmailAddress
{
    public const int MaxLength = 254;

    /// <summary>Normalized form used for uniqueness: <c>Trim()</c> + <c>ToLowerInvariant()</c>.</summary>
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    public static bool IsValid(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            return false;
        }

        // MailAddress also accepts display names ("Name <a@b.c>"); require the bare address.
        return MailAddress.TryCreate(trimmed, out var parsed)
               && parsed.Address == trimmed
               && parsed.Host.Contains('.');
    }

    /// <summary>Validates and returns the trimmed email together with its normalized form.</summary>
    internal static (string Email, string Normalized) Parse(string? email, string name)
    {
        if (!IsValid(email))
        {
            throw new ArgumentException($"{name} must be a valid email address of at most {MaxLength} characters.", name);
        }

        var trimmed = email!.Trim();
        return (trimmed, Normalize(trimmed));
    }
}
