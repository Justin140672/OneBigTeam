namespace HR.Modules.Reporting.ReportRegistry;

/// <summary>
/// Explicit sensitivity classification for a report, per REP-06. Reports that expose employee-level
/// PII or other operationally sensitive detail (names tied to individual records) are
/// <see cref="Sensitive"/>; reports that only expose company-wide aggregates with no named
/// individuals are <see cref="Standard"/>. This drives export auditing policy — see
/// HR.Modules.Reporting.Services.ReportExportAuditor.
/// </summary>
internal enum ReportSensitivity
{
    Standard,
    Sensitive,
}
