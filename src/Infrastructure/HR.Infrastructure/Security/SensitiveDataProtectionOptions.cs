namespace HR.Infrastructure.Security;

/// <summary>
/// Binds from configuration section <c>Infrastructure:SensitiveDataProtection</c>.
///
/// Keys are 32-byte (AES-256) values, base64-encoded, supplied through environment variables or a
/// secret store — never committed to <c>appsettings.json</c> and never stored in the application
/// database. Each environment (dev/staging/prod) supplies its own key set, so a production backup
/// cannot be decrypted with a development key.
///
/// Example environment variables:
/// <code>
/// Infrastructure__SensitiveDataProtection__ActiveKeyId=2026-09
/// Infrastructure__SensitiveDataProtection__Keys__2026-09=&lt;base64 32 bytes&gt;
/// Infrastructure__SensitiveDataProtection__Keys__2026-03=&lt;base64 32 bytes&gt;   (retained for decryption after rotation)
/// </code>
/// </summary>
internal sealed class SensitiveDataProtectionOptions
{
    public const string SectionName = "Infrastructure:SensitiveDataProtection";

    /// <summary>Key id used to encrypt new values. Must be present in <see cref="Keys"/>.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>Map of key id -> base64-encoded 32-byte key. Old key ids are kept here so previously
    /// encrypted values continue to decrypt after a rotation.</summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);
}
