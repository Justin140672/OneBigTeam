using System.Security.Cryptography;
using System.Text;

namespace HR.Modules.Identity.Features.SignUp;

// Canonical, non-secret identity of a signup request used to detect Idempotency-Key reuse.
// The password is deliberately excluded: no password-derived value is ever persisted, so database
// access alone cannot be used to verify guessed passwords offline.
internal static class SignUpIdempotencyMaterial
{
    public static string Fingerprint(SignUpRequest request)
    {
        var canonical = string.Join(
            '\u001f',
            "signup.v2",
            request.CompanyName.Trim(),
            request.AdminFirstName.Trim(),
            request.AdminLastName.Trim(),
            request.AdminEmail.Trim().ToUpperInvariant());

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
