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

    // UseStd3AsciiRules rejects characters that are not valid in host names (e.g. '_', '/', '[').
    private static readonly IdnMapping Idn = new() { AllowUnassigned = false, UseStd3AsciiRules = true };

    /// <summary>
    /// Extracts and normalises the domain of <paramref name="email"/>. Returns false (and an empty
    /// <paramref name="domain"/>) when the address is malformed.
    /// </summary>
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

    /// <summary>
    /// Normalises a bare domain (e.g. a denylist entry or the part after '@'). Returns false (and
    /// an empty <paramref name="domain"/>) when the value is not a syntactically valid host name.
    /// </summary>
    public static bool TryNormalizeDomain(string? value, out string domain)
    {
        domain = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();

        // A single trailing dot denotes the DNS root ("gmail.com.") and is the same domain.
        if (candidate.EndsWith('.'))
            candidate = candidate[..^1];

        if (candidate.Length == 0 || candidate.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return false;

        string ascii;
        try
        {
            // Converts Unicode labels to punycode and applies IDNA mapping (case folding, full-width
            // to ASCII, ...), so visually-equivalent spellings of a blocked domain normalise to the
            // same value as the plain ASCII entry on the denylist.
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

        // Require at least "name.tld" — single-label hosts ("localhost") are never an organisation
        // email domain.
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

        // An all-numeric final label means an IP address literal (e.g. user@192.168.0.1), not a
        // registrable domain.
        if (labels[^1].All(char.IsAsciiDigit))
            return false;

        domain = ascii;
        return true;
    }
}
