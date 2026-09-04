namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Raised when a sensitive value cannot be encrypted or decrypted. The message is deliberately
/// generic and must never contain plaintext, ciphertext or key material so that it is safe to log,
/// surface in telemetry or serialise into a job failure record.
/// </summary>
public sealed class SensitiveDataProtectionException : Exception
{
    public SensitiveDataProtectionException(string message)
        : base(message)
    {
    }

    public SensitiveDataProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
