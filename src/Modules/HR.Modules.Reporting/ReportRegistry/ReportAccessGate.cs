namespace HR.Modules.Reporting.ReportRegistry;

/// <summary>
/// Identifies which per-report authorization check gates access to a given report definition.
/// Mirrors the individual `reporting:view-*` policies already evaluated by GetReportCatalog and
/// the individual report endpoints.
/// </summary>
internal enum ReportAccessGate
{
    Recruitment,
    Hr,
    EmployeeStarter,
    LeaveSummary,
    Probation,
    Onboarding,
    WorkloadActions,
}
