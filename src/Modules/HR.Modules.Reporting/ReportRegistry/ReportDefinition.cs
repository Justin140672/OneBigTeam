using HR.Modules.Reporting.Domain;

namespace HR.Modules.Reporting.ReportRegistry;

/// <summary>
/// Central, authoritative description of a single report: its category/access gate (used by the
/// catalogue and by saved views/favourites authorization) and the set of filter/grouping/sorting
/// field names its own Request/Validator supports (used to validate saved filter JSON).
/// </summary>
/// <param name="Fields">
/// Field name (matching the report's own Request property name) to the set of values allowed for
/// that field, or null when any value of the expected shape is accepted (the field's own report
/// endpoint validator is responsible for deeper value validation at query time).
/// </param>
internal sealed record ReportDefinition(
    string Id,
    string DisplayName,
    ReportCategory Category,
    string Description,
    ReportAccessGate AccessGate,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>?> Fields,
    ReportSensitivity Sensitivity);
