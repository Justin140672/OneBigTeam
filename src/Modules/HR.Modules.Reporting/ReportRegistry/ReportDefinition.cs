using HR.Modules.Reporting.Domain;

namespace HR.Modules.Reporting.ReportRegistry;

internal sealed record ReportDefinition(
    string Id,
    string DisplayName,
    ReportCategory Category,
    string Description,
    ReportAccessGate AccessGate,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>?> Fields,
    ReportSensitivity Sensitivity);
