using System.Security.Cryptography;
using System.Text;
using HR.Infrastructure.Abstractions;

namespace HR.Infrastructure.Security;

/// <summary>
/// AES-256-GCM implementation of <see cref="ISensitiveDataProtector"/>.
///
/// Token format (all ASCII, safe for a Postgres text/varchar column and for transport):
/// <code>
/// OBTENC1:{keyId}:{base64( nonce[12] || ciphertext[n] || tag[16] )}
/// </code>
/// <list type="bullet">
/// <item><description><c>OBTENC1</c> — format version. A future format bump becomes <c>OBTENC2</c>.</description></item>
/// <item><description><c>keyId</c> — identifies which configured key encrypted the value. Decryption
/// selects the key by this id, which is what makes key rotation a config change rather than a data
/// migration. May not contain <c>:</c>.</description></item>
/// <item><description><c>nonce</c> — 96-bit random value, freshly generated per <see cref="Protect"/> call.</description></item>
/// <item><description><c>tag</c> — 128-bit GCM authentication tag. The scheme + key id are bound in as
/// associated data, so swapping the key id in a stored token is detected as tampering.</description></item>
/// </list>
/// The stored value is ciphertext only — it never contains or reveals plaintext.
/// </summary>
internal sealed class AesGcmSensitiveDataProtector : ISensitiveDataProtector
{
    internal const string Scheme = "OBTENC1";
    internal const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly IReadOnlyDictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;

    private AesGcmSensitiveDataProtector(IReadOnlyDictionary<string, byte[]> keys, string activeKeyId)
    {
        _keys = keys;
        _activeKeyId = activeKeyId;
    }

    /// <summary>
    /// Builds a protector from configuration. Throws <see cref="SensitiveDataProtectionException"/>
    /// with a message free of key material when the configuration is missing or invalid.
    /// </summary>
    public static AesGcmSensitiveDataProtector Create(SensitiveDataProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Keys.Count == 0)
            throw new SensitiveDataProtectionException(
                $"No sensitive-data encryption keys are configured. Set '{SensitiveDataProtectionOptions.SectionName}:Keys' via environment or secret configuration.");

        if (string.IsNullOrWhiteSpace(options.ActiveKeyId))
            throw new SensitiveDataProtectionException(
                $"No active sensitive-data encryption key id is configured. Set '{SensitiveDataProtectionOptions.SectionName}:ActiveKeyId'.");

        var parsed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (keyId, encoded) in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(keyId) || keyId.Contains(':'))
                throw new SensitiveDataProtectionException(
                    "A sensitive-data encryption key id is invalid: ids must be non-empty and must not contain ':'.");

            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(encoded?.Trim() ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new SensitiveDataProtectionException(
                    $"The sensitive-data encryption key '{keyId}' is not valid base64.");
            }

            if (keyBytes.Length != KeySizeBytes)
                throw new SensitiveDataProtectionException(
                    $"The sensitive-data encryption key '{keyId}' must be {KeySizeBytes} bytes (AES-256) after base64 decoding.");

            parsed[keyId] = keyBytes;
        }

        if (!parsed.ContainsKey(options.ActiveKeyId))
            throw new SensitiveDataProtectionException(
                $"The active sensitive-data encryption key id '{options.ActiveKeyId}' has no matching entry in '{SensitiveDataProtectionOptions.SectionName}:Keys'.");

        return new AesGcmSensitiveDataProtector(parsed, options.ActiveKeyId);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var key = _keys[_activeKeyId];
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];
        var associatedData = Encoding.ASCII.GetBytes($"{Scheme}:{_activeKeyId}");

        using (var aes = new AesGcm(key, TagSizeBytes))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedData);
        }

        var blob = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, blob, NonceSizeBytes, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, blob, NonceSizeBytes + ciphertext.Length, TagSizeBytes);

        return $"{Scheme}:{_activeKeyId}:{Convert.ToBase64String(blob)}";
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        var parts = protectedValue.Split(':', 3);
        if (parts.Length != 3 || parts[0] != Scheme)
            throw new SensitiveDataProtectionException("The value is not a recognised protected token.");

        var keyId = parts[1];
        if (!_keys.TryGetValue(keyId, out var key))
            throw new SensitiveDataProtectionException(
                $"No sensitive-data encryption key is configured for key id '{keyId}'. It may have been removed after a rotation.");

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            throw new SensitiveDataProtectionException("The protected token payload is not valid base64.");
        }

        if (blob.Length < NonceSizeBytes + TagSizeBytes)
            throw new SensitiveDataProtectionException("The protected token payload is too short.");

        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[blob.Length - NonceSizeBytes - TagSizeBytes];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(blob, NonceSizeBytes, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(blob, NonceSizeBytes + ciphertext.Length, tag, 0, TagSizeBytes);

        var plaintextBytes = new byte[ciphertext.Length];
        var associatedData = Encoding.ASCII.GetBytes($"{Scheme}:{keyId}");

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes, associatedData);
        }
        catch (CryptographicException ex)
        {
            throw new SensitiveDataProtectionException(
                "Authentication of the protected value failed. The ciphertext was tampered with or was encrypted with a different key.", ex);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public bool IsProtected(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var parts = value.Split(':', 3);
        return parts.Length == 3 && parts[0] == Scheme && parts[1].Length > 0 && parts[2].Length > 0;
    }

    public bool TryUnprotect(string? value, out string? plaintext)
    {
        plaintext = null;
        if (!IsProtected(value))
            return false;

        try
        {
            plaintext = Unprotect(value!);
            return true;
        }
        catch (SensitiveDataProtectionException)
        {
            plaintext = null;
            return false;
        }
    }
}
