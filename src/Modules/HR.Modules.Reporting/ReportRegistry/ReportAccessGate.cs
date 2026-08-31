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

    // ADM-08: administrative governance reporting hub. Backed by the "reporting:view-governance"
    // policy; every governance report endpoint also requires baseline "reporting:view".
    Governance,
}
