namespace HR.Infrastructure.Security;

internal sealed class SensitiveDataProtectionOptions
{
    public const string SectionName = "Infrastructure:SensitiveDataProtection";

    public string ActiveKeyId { get; set; } = string.Empty;

    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);
}
