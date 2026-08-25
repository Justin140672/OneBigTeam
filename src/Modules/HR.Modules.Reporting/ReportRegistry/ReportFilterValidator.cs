using System.Text.Json;
using HR.SharedKernel;

namespace HR.Modules.Reporting.ReportRegistry;

/// <summary>
/// Validates saved filter-criteria JSON (used by SaveReportView) against the field names/allowed
/// values a given report definition actually supports, and rejects malformed or oversized payloads
/// cleanly instead of throwing.
/// </summary>
internal static class ReportFilterValidator
{
    /// <summary>
    /// Reasonable upper bound for a saved filter payload. Saved views only ever hold a handful of
    /// scalar filter/grouping/sorting fields, so this comfortably covers legitimate use while
    /// rejecting pathological/oversized payloads before they reach JSON parsing.
    /// </summary>
    public const int MaxFilterCriteriaJsonLength = 8_000;

    public static Result Validate(ReportDefinition definition, string filterCriteriaJson)
    {
        if (string.IsNullOrWhiteSpace(filterCriteriaJson))
            return Result.Failure(Error.Validation("Filter criteria must not be empty."));

        if (filterCriteriaJson.Length > MaxFilterCriteriaJsonLength)
            return Result.Failure(Error.Validation(
                $"Filter criteria exceeds the maximum allowed size of {MaxFilterCriteriaJsonLength} characters."));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(filterCriteriaJson);
        }
        catch (JsonException)
        {
            return Result.Failure(Error.Validation("Filter criteria must be valid JSON."));
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Result.Failure(Error.Validation("Filter criteria must be a JSON object."));

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!definition.Fields.TryGetValue(property.Name, out var allowedValues))
                    return Result.Failure(Error.Validation(
                        $"'{property.Name}' is not a supported filter, grouping or sorting field for report '{definition.Id}'."));

                if (allowedValues is null)
                    continue;

                if (property.Value.ValueKind == JsonValueKind.Null)
                    continue;

                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();

                if (value is not null && !allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
                    return Result.Failure(Error.Validation(
                        $"'{value}' is not a supported value for '{property.Name}' on report '{definition.Id}'."));
            }
        }

        return Result.Success();
    }
}
