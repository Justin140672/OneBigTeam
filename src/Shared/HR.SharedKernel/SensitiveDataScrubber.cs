using System.Text.RegularExpressions;

namespace HR.SharedKernel;

/// <summary>
/// NFR-01: single source of truth for sensitive-data classification, shared by the audit
/// payload redaction guard, structured-logging enricher and OpenTelemetry trace processor.
///
/// Two independent checks are provided:
/// <list type="bullet">
/// <item><description>
/// <see cref="IsProhibitedFieldName"/> — the field/property/tag <b>name</b> is inherently
/// sensitive (e.g. <c>salary</c>, <c>nationalInsuranceNumber</c>, <c>bankAccountNumber</c>,
/// <c>password</c>, <c>token</c>).
/// </description></item>
/// <item><description>
/// <see cref="ContainsSensitiveValue"/> / <see cref="ScrubText"/> — the <b>value</b> looks
/// like a sensitive token regardless of the field it sits in (NI number, IBAN, sort code,
/// bank/card number, bearer token, JWT, bcrypt/argon hash).
/// </description></item>
/// </list>
/// </summary>
public static class SensitiveDataScrubber
{
    public const string Redacted = "***REDACTED***";

    /// <summary>
    /// Ticket 1: prefix of an application-level encrypted value
    /// (<c>OBTENC1:{keyId}:{base64}</c>). Such a value is ciphertext and safe to persist or log as-is;
    /// the plaintext it protects is still governed by the field-name and value rules below and must
    /// never be logged after decryption.
    /// </summary>
    public const string ProtectedValuePrefix = "OBTENC1:";

    /// <summary>Returns true when <paramref name="value"/> is an already-encrypted protected token.</summary>
    public static bool IsProtectedValue(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Field names that must never carry a value in an audit payload, log property or trace tag.
    /// Exact, case-insensitive matches. Boolean display-preference flags such as
    /// <c>DisplaySalaryOnEmployeeProfile</c> are deliberately not exact matches here.
    /// </summary>
    public static readonly IReadOnlyCollection<string> ProhibitedFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Compensation / financial amounts
        "salary", "previousSalary", "currentSalary", "newSalary", "oldSalary",
        "salaryAmount", "annualSalary", "baseSalary", "proposedSalary", "grossSalary",
        "compensation", "compensationAmount", "payAmount", "hourlyRate", "dayRate",
        "bonus", "bonusAmount",
        // Tax / government identifiers
        "nationalInsuranceNumber", "niNumber", "ni", "nino", "taxCode", "taxIdentifier", "utr",
        // Banking
        "bankAccountNumber", "accountNumber", "sortCode", "iban", "bankAccount", "bic", "swift",
        "cardNumber", "cvv",
        // Authentication / credentials
        "password", "passwordHash", "token", "secret", "clientSecret", "apiKey",
        "refreshToken", "accessToken", "bearerToken", "authorization", "credentials",
        "privateKey", "connectionString",
        // Personal identifiers / contact
        "dateOfBirth", "dob", "personalEmail", "personalPhone", "personalPhoneNumber", "homeAddress",
        // Medical / sickness
        "medicalNote", "sicknessNote", "diagnosisNote", "diagnosisCode",
    };

    /// <summary>
    /// Substrings that make any compound field name sensitive (e.g. <c>BankAccountSortCode</c>).
    /// </summary>
    public static readonly IReadOnlyCollection<string> ProhibitedNameFragments = new[]
    {
        "nationalinsurance", "national_insurance",
        "bankaccount", "bank_account",
        "passwordhash", "password_hash",
    };

    private static readonly (string Name, Regex Pattern)[] ValuePatterns =
    [
        // UK National Insurance number, with or without spaces (QQ 12 34 56 C).
        // Deliberately liberal on the two-letter prefix (a redactor should over-match rather than
        // leak): two letters + 6 digits (optionally space-separated) + a trailing A-D suffix.
        ("NationalInsuranceNumber", new Regex(
            @"\b[A-Z]{2}\s?\d{2}\s?\d{2}\s?\d{2}\s?[A-D]\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        // IBAN (2 letter country + 2 check digits + up to 30 alnum). Case-sensitive (uppercase)
        // deliberately: real-world IBANs are always written/stored in uppercase, and matching
        // case-insensitively made this trip on ordinary lowercase-hex GUID fragments embedded in
        // names/emails/slugs (e.g. "Policy-cb0754f9d9ab..." from Guid.NewGuid():N) — "cb" + "07"
        // + 11-30 more hex chars satisfies [A-Z]{2}\d{2}[A-Z0-9]{11,30} case-insensitively even
        // though it's nowhere near an IBAN. A lowercase IBAN pasted into a payload is vanishingly
        // rare in practice and not worth the false-positive rate this caused.
        ("Iban", new Regex(
            @"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b",
            RegexOptions.Compiled)),
        // UK sort code 12-34-56.
        ("SortCode", new Regex(@"\b\d{2}-\d{2}-\d{2}\b", RegexOptions.Compiled)),
        // Bank account / payment card number: 12-19 consecutive digits.
        ("BankOrCardNumber", new Regex(@"\b\d{12,19}\b", RegexOptions.Compiled)),
        // Bearer token in an Authorization header value.
        ("BearerToken", new Regex(
            @"(?i)\bbearer\s+[A-Za-z0-9\-._~+/]+=*",
            RegexOptions.Compiled)),
        // JSON Web Token.
        ("Jwt", new Regex(
            @"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\b",
            RegexOptions.Compiled)),
        // bcrypt password hash.
        ("BcryptHash", new Regex(
            @"\$2[aby]\$\d{2}\$[./A-Za-z0-9]{53}",
            RegexOptions.Compiled)),
        // argon2 / PHC-style password hash.
        ("Argon2Hash", new Regex(
            @"\$argon2(id|i|d)\$[^\s""]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    ];

    public static bool IsProhibitedFieldName(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return false;

        if (ProhibitedFieldNames.Contains(fieldName))
            return true;

        foreach (var fragment in ProhibitedNameFragments)
        {
            if (fieldName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Returns the name of the first sensitive value pattern matched, or null.</summary>
    public static string? MatchSensitiveValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // A value that is exactly a GUID is never a bank account / card / NI number / token —
        // but a GUID's 12-hex final segment (or an all-zero seeded id like
        // 00000000-0000-0000-0000-000000000001) trivially trips the "12-19 consecutive digits"
        // BankOrCardNumber pattern. Audit payloads and structured logs are full of GUID entity
        // ids, so treat a standalone GUID as non-sensitive rather than over-matching it.
        var trimmed = value.Trim();
        if (Guid.TryParse(trimmed, out _))
            return null;

        foreach (var (name, pattern) in ValuePatterns)
        {
            if (pattern.IsMatch(value))
                return name;
        }

        return null;
    }

    public static bool ContainsSensitiveValue(string? value) => MatchSensitiveValue(value) is not null;

    /// <summary>
    /// Masks an email address for logging: keeps up to the first two characters of the local part
    /// and the full domain (e.g. <c>ja***@example.com</c>). Returns <see cref="Redacted"/> when the
    /// value is null/blank or not a recognisable <c>local@domain</c> address.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Redacted;

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1)
            return Redacted;

        var local = trimmed[..at];
        var domain = trimmed[(at + 1)..];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}***@{domain}";
    }

    /// <summary>Replaces every sensitive-looking token in <paramref name="text"/> with <see cref="Redacted"/>.</summary>
    public static string ScrubText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var result = text;
        foreach (var (_, pattern) in ValuePatterns)
            result = pattern.Replace(result, Redacted);

        return result;
    }
}
