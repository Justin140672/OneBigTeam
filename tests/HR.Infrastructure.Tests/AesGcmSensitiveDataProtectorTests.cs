using System.Security.Cryptography;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Security;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Ticket 1: application-level AES-256-GCM protection of sensitive values.
/// Exercises round-tripping, non-disclosure of plaintext, tamper detection,
/// key rotation/versioning and configuration validation.
/// </summary>
public class AesGcmSensitiveDataProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmSensitiveDataProtector Build(string activeKeyId, params (string Id, string Key)[] keys)
    {
        var options = new SensitiveDataProtectionOptions { ActiveKeyId = activeKeyId };
        foreach (var (id, key) in keys)
            options.Keys[id] = key;
        return AesGcmSensitiveDataProtector.Create(options);
    }

    private static AesGcmSensitiveDataProtector BuildDefault(string activeKeyId = "k1")
        => Build(activeKeyId, (activeKeyId, NewKey()));

    // 1. Round-trip
    [Theory]
    [InlineData("hello world")]
    [InlineData("")]
    [InlineData("unicode: éèê 你好 😀 ❤️")]
    public void Protect_then_Unprotect_returns_original(string plaintext)
    {
        var protector = BuildDefault();

        var token = protector.Protect(plaintext);

        Assert.Equal(plaintext, protector.Unprotect(token));
    }

    [Fact]
    public void Protect_then_Unprotect_returns_original_for_long_string()
    {
        var protector = BuildDefault();
        var plaintext = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 500));

        var token = protector.Protect(plaintext);

        Assert.Equal(plaintext, protector.Unprotect(token));
    }

    // 2. Token shape + plaintext never present
    [Fact]
    public void Protect_output_is_prefixed_and_does_not_contain_plaintext()
    {
        var protector = Build("2026-09", ("2026-09", NewKey()));
        const string plaintext = "Distinctive-SECRET-VALUE-Zaphod-Beeblebrox-42";

        var token = protector.Protect(plaintext);

        Assert.StartsWith("OBTENC1:2026-09:", token);
        Assert.DoesNotContain(plaintext, token, StringComparison.Ordinal);
    }

    // 3. Random nonce -> different tokens, both decrypt
    [Fact]
    public void Two_Protect_calls_produce_different_tokens_that_both_Unprotect()
    {
        var protector = BuildDefault();
        const string plaintext = "same input";

        var first = protector.Protect(plaintext);
        var second = protector.Protect(plaintext);

        Assert.NotEqual(first, second);
        Assert.Equal(plaintext, protector.Unprotect(first));
        Assert.Equal(plaintext, protector.Unprotect(second));
    }

    // 4. Tampered ciphertext
    [Fact]
    public void Unprotect_throws_when_payload_bytes_are_tampered()
    {
        var protector = BuildDefault();
        var token = protector.Protect("authentic message");

        var parts = token.Split(':', 3);
        var blob = Convert.FromBase64String(parts[2]);
        blob[blob.Length / 2] ^= 0xFF; // flip a middle byte (inside the ciphertext region)
        var tampered = $"{parts[0]}:{parts[1]}:{Convert.ToBase64String(blob)}";

        Assert.Throws<SensitiveDataProtectionException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_throws_when_base64_char_is_flipped()
    {
        var protector = BuildDefault();
        var token = protector.Protect("authentic message");

        var parts = token.Split(':', 3);
        var payload = parts[2].ToCharArray();
        var idx = payload.Length / 2;
        payload[idx] = payload[idx] == 'A' ? 'B' : 'A';
        var tampered = $"{parts[0]}:{parts[1]}:{new string(payload)}";

        Assert.Throws<SensitiveDataProtectionException>(() => protector.Unprotect(tampered));
    }

    // 5. Wrong key under the same key id
    [Fact]
    public void Unprotect_throws_when_key_bytes_differ_for_same_key_id()
    {
        var protectorA = Build("k1", ("k1", NewKey()));
        var token = protectorA.Protect("cross-key message");

        var protectorB = Build("k1", ("k1", NewKey()));

        Assert.Throws<SensitiveDataProtectionException>(() => protectorB.Unprotect(token));
    }

    // 6. Key rotation / versioning
    [Fact]
    public void Rotated_protector_still_decrypts_old_token_and_tags_new_values_with_active_key()
    {
        var kOld = NewKey();
        var kNew = NewKey();
        const string plaintext = "rotate me";

        var oldProtector = Build("2024-01", ("2024-01", kOld));
        var oldToken = oldProtector.Protect(plaintext);

        var rotated = Build("2025-06", ("2024-01", kOld), ("2025-06", kNew));

        Assert.Equal(plaintext, rotated.Unprotect(oldToken));

        var freshToken = rotated.Protect(plaintext);
        Assert.StartsWith("OBTENC1:2025-06:", freshToken);
        Assert.Equal(plaintext, rotated.Unprotect(freshToken));
    }

    // 7. Old key removed after rotation
    [Fact]
    public void Unprotect_throws_when_the_tokens_key_id_is_no_longer_configured()
    {
        var kOld = NewKey();
        var kNew = NewKey();

        var oldProtector = Build("2024-01", ("2024-01", kOld));
        var oldToken = oldProtector.Protect("stranded");

        var newOnly = Build("2025-06", ("2025-06", kNew));

        Assert.Throws<SensitiveDataProtectionException>(() => newOnly.Unprotect(oldToken));
    }

    // 8. Missing / invalid key configuration
    [Fact]
    public void Create_throws_when_no_keys_configured()
    {
        var options = new SensitiveDataProtectionOptions { ActiveKeyId = "k1" };

        Assert.Throws<SensitiveDataProtectionException>(() => AesGcmSensitiveDataProtector.Create(options));
    }

    [Fact]
    public void Create_throws_when_active_key_id_is_not_in_keys()
    {
        var options = new SensitiveDataProtectionOptions { ActiveKeyId = "missing" };
        options.Keys["k1"] = NewKey();

        Assert.Throws<SensitiveDataProtectionException>(() => AesGcmSensitiveDataProtector.Create(options));
    }

    [Fact]
    public void Create_throws_when_a_key_value_is_not_base64()
    {
        var options = new SensitiveDataProtectionOptions { ActiveKeyId = "k1" };
        options.Keys["k1"] = "not valid base64 !!!";

        Assert.Throws<SensitiveDataProtectionException>(() => AesGcmSensitiveDataProtector.Create(options));
    }

    [Fact]
    public void Create_throws_when_a_key_decodes_to_wrong_length()
    {
        var options = new SensitiveDataProtectionOptions { ActiveKeyId = "k1" };
        options.Keys["k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)); // AES-128 sized

        Assert.Throws<SensitiveDataProtectionException>(() => AesGcmSensitiveDataProtector.Create(options));
    }

    // 9. IsProtected
    [Fact]
    public void IsProtected_true_for_real_token()
    {
        var protector = BuildDefault();

        Assert.True(protector.IsProtected(protector.Protect("x")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("just a plain string")]
    [InlineData("OBTENC1:")]
    public void IsProtected_false_for_non_tokens(string? value)
    {
        var protector = BuildDefault();

        Assert.False(protector.IsProtected(value));
    }

    // 10. TryUnprotect
    [Fact]
    public void TryUnprotect_returns_true_and_plaintext_for_valid_token()
    {
        var protector = BuildDefault();
        var token = protector.Protect("try me");

        var ok = protector.TryUnprotect(token, out var plaintext);

        Assert.True(ok);
        Assert.Equal("try me", plaintext);
    }

    [Fact]
    public void TryUnprotect_returns_false_and_null_for_plain_string()
    {
        var protector = BuildDefault();

        var ok = protector.TryUnprotect("not a token", out var plaintext);

        Assert.False(ok);
        Assert.Null(plaintext);
    }

    [Fact]
    public void TryUnprotect_returns_false_and_null_for_tampered_token()
    {
        var protector = BuildDefault();
        var token = protector.Protect("try me");
        var parts = token.Split(':', 3);
        var blob = Convert.FromBase64String(parts[2]);
        blob[blob.Length / 2] ^= 0xFF;
        var tampered = $"{parts[0]}:{parts[1]}:{Convert.ToBase64String(blob)}";

        var ok = protector.TryUnprotect(tampered, out var plaintext);

        Assert.False(ok);
        Assert.Null(plaintext);
    }

    // 11. Malformed tokens
    [Theory]
    [InlineData("not-a-token")]
    [InlineData("OBTENC1:k1:not-base64!!")]
    [InlineData("OBTENC2:k1:AAAA")]
    public void Unprotect_throws_for_malformed_token(string value)
    {
        var protector = BuildDefault();

        Assert.Throws<SensitiveDataProtectionException>(() => protector.Unprotect(value));
    }
}
