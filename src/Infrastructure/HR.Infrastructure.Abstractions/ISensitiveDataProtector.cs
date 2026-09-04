namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Ticket 1: application-level authenticated encryption for sensitive persisted values
/// (salary, bank details, National Insurance numbers, etc.).
///
/// Encryption and decryption happen in the application layer — before a value is written to
/// PostgreSQL and after it is read back — so that a database backup on its own never reveals
/// plaintext. Keys are supplied through environment/secret configuration and are never stored
/// in the application database.
///
/// The produced token is self-describing and carries a format version and a key id, so keys can
/// be rotated without a data migration: old values keep decrypting with their embedded key id
/// while new values are written with the active key.
/// </summary>
public interface ISensitiveDataProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with the active key using AES-256-GCM and returns a
    /// self-describing token of the form <c>OBTENC1:{keyId}:{base64(nonce|ciphertext|tag)}</c>.
    /// </summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a token produced by <see cref="Protect"/>. The key is selected from the key id
    /// embedded in the token. Throws <see cref="SensitiveDataProtectionException"/> when the token
    /// is malformed, the referenced key is not configured, or the authentication tag fails
    /// (tampered ciphertext or wrong key). The exception never contains plaintext or key material.
    /// </summary>
    string Unprotect(string protectedValue);

    /// <summary>
    /// Returns true when <paramref name="value"/> looks like a token produced by <see cref="Protect"/>.
    /// Used to make read paths tolerant of not-yet-migrated plaintext during a field roll-out.
    /// </summary>
    bool IsProtected(string? value);

    /// <summary>
    /// Attempts to decrypt <paramref name="value"/>. Returns false (without throwing) when the value
    /// is null, not a protected token, or cannot be decrypted.
    /// </summary>
    bool TryUnprotect(string? value, out string? plaintext);
}
