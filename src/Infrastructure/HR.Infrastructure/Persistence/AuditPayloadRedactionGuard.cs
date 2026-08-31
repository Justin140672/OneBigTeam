using System.Text.Json;
using HR.SharedKernel;

namespace HR.Infrastructure.Persistence;

/// <summary>
/// AUD-03 / NFR-01: centralised sensitive-data classification and rejection.
///
/// Scans serialised audit Before/After/Metadata JSON before the pending item is written and
/// rejects the payload if it contains either:
/// <list type="bullet">
/// <item><description>a prohibited field <b>name</b> (salary, NI number, bank details, password,
/// token, secret, ...) — see <see cref="SensitiveDataScrubber.ProhibitedFieldNames"/>; or</description></item>
/// <item><description>a string <b>value</b> that matches a sensitive pattern (NI number, IBAN,
/// sort code, bank/card number, bearer token, JWT, bcrypt/argon hash) regardless of the field
/// name it sits under.</description></item>
/// </list>
///
/// Throws <see cref="ProhibitedAuditFieldException"/> so the publisher can log a clear
/// operational error; the offending payload is never persisted. Publishers must fix the audit
/// event to omit the value. Where a change must still be recorded (e.g. "salary was changed"),
/// use a summary-only approach (direction / band, never the amount).
/// </summary>
internal static class AuditPayloadRedactionGuard
{
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
                $"NFR-01: audit {fieldName} payload could not be parsed for sensitive-field validation.");
        }

        using (doc)
        {
            CheckElement(doc.RootElement, fieldName);
        }
    }

    private static void CheckElement(JsonElement element, string context)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CheckPropertyName(property.Name, context);
                    CheckElement(property.Value, context);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CheckElement(item, context);
                break;

            case JsonValueKind.String:
                CheckValue(element.GetString(), context);
                break;
        }
    }

    private static void CheckPropertyName(string propertyName, string context)
    {
        if (SensitiveDataScrubber.IsProhibitedFieldName(propertyName))
        {
            throw new ProhibitedAuditFieldException(
                $"NFR-01: prohibited field '{propertyName}' found in audit {context} payload. " +
                $"Remove this field — use a summary-only approach (direction/band, never the amount) for sensitive values.");
        }
    }

    private static void CheckValue(string? value, string context)
    {
        var match = SensitiveDataScrubber.MatchSensitiveValue(value);
        if (match is not null)
        {
            throw new ProhibitedAuditFieldException(
                $"NFR-01: audit {context} payload contains a value matching sensitive pattern '{match}'. " +
                $"Remove the value from the audit event.");
        }
    }
}

/// <summary>
/// Thrown by <see cref="AuditPayloadRedactionGuard"/> when a prohibited field or value is detected.
/// The publisher logs this and the pending item is not persisted.
/// </summary>
public sealed class ProhibitedAuditFieldException(string message) : InvalidOperationException(message);
