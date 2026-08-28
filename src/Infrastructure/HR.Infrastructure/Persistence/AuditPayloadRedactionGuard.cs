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
    /// Field name fragments that are unconditionally prohibited in any audit payload.
    /// Matching is case-insensitive and checks for substring containment so that
    /// PreviousSalary, CurrentSalary, SalaryAmount, etc. are all caught by "salary".
    /// </summary>
    private static readonly string[] ProhibitedFragments =
    [
        "salary",
        "compensation",
        "nationalinsurance",
        "national_insurance",
        "niNumber",
        "ni_number",
        "bankaccount",
        "bank_account",
        "sortcode",
        "sort_code",
        "taxcode",
        "tax_code",
        "password",
        "token",
        "secret",
        "dateofbirth",
        "date_of_birth",
        "dob",
        "personalphone",
        "personal_phone",
        "personalemail",
        "personal_email",
        "homeaddress",
        "home_address",
        "medicalNote",
        "medical_note",
        "sicknessNote",
        "sickness_note",
        "diagnosisNote",
        "diagnosis_note",
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
        foreach (var fragment in ProhibitedFragments)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                throw new ProhibitedAuditFieldException(
                    $"AUD-03: prohibited field '{propertyName}' found in audit {context} payload. " +
                    $"Remove this field from the audit event — use a summary-only approach for sensitive values.");
            }
        }
    }
}

/// <summary>
/// Thrown by <see cref="AuditPayloadRedactionGuard"/> when a prohibited field is detected.
/// The publisher logs this and the pending item is not persisted.
/// </summary>
public sealed class ProhibitedAuditFieldException(string message) : InvalidOperationException(message);
