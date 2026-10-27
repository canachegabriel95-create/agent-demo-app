namespace ContentReview.Infrastructure.Auditing;

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>
    /// What happens when audit details contain something sensitive (a password, token, hash or full invitation code):
    /// <c>true</c> throws, so the mistake shows up while developing; <c>false</c> removes the value and writes the event
    /// anyway, so a mistake never loses the audit trail. Unset means "throw in Development only".
    /// </summary>
    public bool? ThrowOnSensitiveDetails { get; set; }
}
