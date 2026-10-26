namespace ContentReview.Domain.Common;

/// <summary>Argument validation shared by the entity factory methods.</summary>
internal static class Guard
{
    /// <summary>Trims <paramref name="value"/> and requires its length to be within [min, max].</summary>
    public static string Text(string? value, string name, int min, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        if (trimmed.Length < min || trimmed.Length > max)
        {
            throw new ArgumentException($"{name} must be between {min} and {max} characters.", name);
        }

        return trimmed;
    }

    /// <summary>Trims an optional value; blank becomes <c>null</c>. Length must not exceed <paramref name="max"/>.</summary>
    public static string? OptionalText(string? value, string name, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > max)
        {
            throw new ArgumentException($"{name} must be at most {max} characters.", name);
        }

        return trimmed;
    }

    /// <summary>Text that may be empty (defaults to empty) but not longer than <paramref name="max"/>. Not trimmed.</summary>
    public static string Body(string? value, string name, int max)
    {
        var text = value ?? string.Empty;
        if (text.Length > max)
        {
            throw new ArgumentException($"{name} must be at most {max} characters.", name);
        }

        return text;
    }

    public static TEnum Defined<TEnum>(TEnum value, string name) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(name, value, $"{value} is not a valid {typeof(TEnum).Name}.");
        }

        return value;
    }

    public static Guid NotEmpty(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value;
    }

    /// <summary>Timestamps are persisted as <c>timestamptz</c> in UTC.</summary>
    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
}
