using System.Text.Json;

namespace HR.Infrastructure.Persistence;

/// <summary>
/// AUD-03: centralised sensitive-data classification and redaction.
///
/// Scans serialised audit Before/After/Metadata JSON for prohibited field names before the
/// pending item is written. Throws <see cref="ProhibitedAuditFieldException"/> so the publisher
/// can log a clear operational error and the offending payload is never persisted.
///
/// Publishers must fix the audit event to omit prohibited fields. Where a change must still be
/// recorded (e.g. "salary was changed"), use a summary-only approach (no value in payload).
/// </summary>
internal static class AuditPayloadRedactionGuard
{
    /// <summary>
    /// Field names that are prohibited in any audit payload — exact case-insensitive matches.
    /// These are VALUE-bearing fields; boolean display-preference flags (e.g.
    /// DisplaySalaryOnEmployeeProfile) are not prohibited by exact match.
    /// </summary>
    private static readonly HashSet<string> ExactProhibitedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Compensation / financial
        "salary",
        "previousSalary",
        "currentSalary",
        "salaryAmount",
        "annualSalary",
        "baseSalary",
        "proposedSalary",
        "compensation",
        "compensationAmount",
        // Tax / government identifiers
        "nationalInsuranceNumber",
        "niNumber",
        "ni",
        "taxCode",
        "taxIdentifier",
        // Banking
        "bankAccountNumber",
        "accountNumber",
        "sortCode",
        "iban",
        "bankAccount",
        // Authentication
        "password",
        "passwordHash",
        "token",
        "secret",
        "refreshToken",
        "accessToken",
        // Personal identifiers / contact
        "dateOfBirth",
        "dob",
        "personalEmail",
        "personalPhone",
        "personalPhoneNumber",
        "homeAddress",
        // Medical / sickness
        "medicalNote",
        "sicknessNote",
        "diagnosisNote",
        "diagnosisCode",
    };

    /// <summary>
    /// Fragment patterns that are always prohibited regardless of prefix/suffix.
    /// Only used for patterns where any compound name is sensitive
    /// (e.g. "bankaccount" in BankAccountSortCode).
    /// </summary>
    private static readonly string[] ProhibitedFragments =
    [
        "nationalinsurance",
        "national_insurance",
        "bankaccount",
        "bank_account",
    ];

    public static void AssertPayloadIsSafe(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            // If we cannot parse the JSON we cannot scan it — fail safe and reject.
            throw new ProhibitedAuditFieldException(
                $"AUD-03: audit {fieldName} payload could not be parsed for sensitive-field validation.");
        }

        using (doc)
        {
            CheckElement(doc.RootElement, fieldName);
        }
    }

    private static void CheckElement(JsonElement element, string context)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                CheckPropertyName(property.Name, context);
                CheckElement(property.Value, context);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                CheckElement(item, context);
        }
    }

    private static void CheckPropertyName(string propertyName, string context)
    {
        if (ExactProhibitedNames.Contains(propertyName))
        {
            throw new ProhibitedAuditFieldException(
                $"AUD-03: prohibited field '{propertyName}' found in audit {context} payload. " +
                $"Remove this field — use a summary-only approach for sensitive values.");
        }

        foreach (var fragment in ProhibitedFragments)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                throw new ProhibitedAuditFieldException(
                    $"AUD-03: prohibited field pattern '{fragment}' matched by '{propertyName}' in audit {context} payload. " +
                    $"Remove this field from the audit event.");
            }
        }
    }
}

/// <summary>
/// Thrown by <see cref="AuditPayloadRedactionGuard"/> when a prohibited field is detected.
/// The publisher logs this and the pending item is not persisted.
/// </summary>
public sealed class ProhibitedAuditFieldException(string message) : InvalidOperationException(message);
