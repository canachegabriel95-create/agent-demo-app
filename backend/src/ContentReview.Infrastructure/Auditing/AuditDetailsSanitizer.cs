using System.Text.Json.Nodes;
using ContentReview.Domain.Common;

namespace ContentReview.Infrastructure.Auditing;

/// <summary>An audit event's details held something that must never be audited.</summary>
public sealed class SensitiveAuditDetailsException(string path)
    : InvalidOperationException($"Audit details must not contain sensitive data (found at '{path}'). Record a non-secret form instead, e.g. codeSuffix.")
{
    public string Path { get; } = path;
}

/// <summary>
/// Guards the details of an audit event against secrets. A property is sensitive when its name says so
/// (password, token, hash, secret, or a code other than <c>codeSuffix</c>), and a text value is sensitive when it is a full
/// invitation code, whatever the property is called.
/// </summary>
public static class AuditDetailsSanitizer
{
    private static readonly string[] SensitiveNameParts = ["password", "token", "hash", "secret"];

    public static bool IsSensitiveName(string name)
    {
        var lower = name.ToLowerInvariant();
        if (SensitiveNameParts.Any(lower.Contains))
        {
            return true;
        }

        // "code" and "invitationCode" are secrets; "codeSuffix" (the last 4 characters) is the allowed form.
        return lower.EndsWith("code", StringComparison.Ordinal);
    }

    public static bool IsInvitationCode(string value) => InvitationCode.Normalize(value) is not null;

    private static bool IsCodeValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && IsInvitationCode(text);

    /// <summary>Returns the details without sensitive properties or values (they are removed), or throws.</summary>
    /// <exception cref="SensitiveAuditDetailsException">Something sensitive was found and <paramref name="throwOnSensitive"/> is set.</exception>
    public static JsonNode? Sanitize(JsonNode? details, bool throwOnSensitive)
    {
        switch (details)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (IsSensitiveName(name) || IsCodeValue(value))
                    {
                        if (throwOnSensitive)
                        {
                            throw new SensitiveAuditDetailsException(name);
                        }

                        obj.Remove(name);
                        continue;
                    }

                    Sanitize(value, throwOnSensitive);
                }

                break;
            case JsonArray array:
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    if (IsCodeValue(array[i]))
                    {
                        if (throwOnSensitive)
                        {
                            throw new SensitiveAuditDetailsException($"[{i}]");
                        }

                        array.RemoveAt(i);
                        continue;
                    }

                    Sanitize(array[i], throwOnSensitive);
                }

                break;
        }

        return details;
    }
}
