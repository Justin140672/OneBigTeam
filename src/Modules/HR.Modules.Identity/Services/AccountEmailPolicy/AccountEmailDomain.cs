using System.Globalization;

namespace HR.Modules.Identity.Services.AccountEmailPolicy;

/// <summary>
/// Ticket 9: safe extraction and normalisation of the domain part of an email address, shared by
/// the account-creation email-domain policy (for the submitted address) and by the denylist loader
/// (for every configured entry), so both sides are always compared in exactly the same canonical
/// form: trimmed, lower-case, trailing root dot removed, and IDN labels converted to their ASCII
/// (punycode) form via <see cref="IdnMapping"/>.
///
/// Anything that cannot be normalised with confidence (no/multiple '@', whitespace or control
/// characters, empty labels, IP-literal domains, invalid IDN input, ...) is reported as malformed
/// so callers can reject it before any domain evaluation — a malformed address must never be
/// treated as "not on the denylist, therefore allowed".
/// </summary>
internal static class AccountEmailDomain
{
    private const int MaxDomainLength = 253;
    private const int MaxLabelLength = 63;

    private static readonly IdnMapping Idn = new() { AllowUnassigned = false, UseStd3AsciiRules = true };

    public static bool TryExtractDomain(string? email, out string domain)
    {
        domain = string.Empty;

        if (string.IsNullOrWhiteSpace(email))
            return false;

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');

        // Exactly one '@', with a non-empty local part and a non-empty domain part. Quoted local
        // parts containing '@' are legal in RFC 5322 but are never needed for an account login and
        // would make "which part is the domain?" ambiguous, so they are rejected.
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
            return false;

        var localPart = trimmed[..at];
        if (localPart.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return false;

        return TryNormalizeDomain(trimmed[(at + 1)..], out domain);
    }

    public static bool TryNormalizeDomain(string? value, out string domain)
    {
        domain = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();

        if (candidate.EndsWith('.'))
            candidate = candidate[..^1];

        if (candidate.Length == 0 || candidate.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return false;

        string ascii;
        try
        {
            ascii = Idn.GetAscii(candidate);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            // Fail closed if the host has no IDN support (e.g. globalization-invariant mode): the
            // address cannot be evaluated reliably, so it is treated as malformed and rejected.
            return false;
        }

        ascii = ascii.ToLowerInvariant();

        if (ascii.Length > MaxDomainLength)
            return false;

        var labels = ascii.Split('.');

        if (labels.Length < 2)
            return false;

        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > MaxLabelLength)
                return false;

            if (label[0] == '-' || label[^1] == '-')
                return false;

            if (!label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                return false;
        }

        if (labels[^1].All(char.IsAsciiDigit))
            return false;

        domain = ascii;
        return true;
    }
}
