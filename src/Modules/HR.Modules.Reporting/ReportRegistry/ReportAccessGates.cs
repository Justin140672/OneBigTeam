namespace HR.Modules.Reporting.ReportRegistry;

/// <summary>
/// The caller's currently-evaluated authorization outcome for every per-report access gate.
/// Constructed once per request from the same `reporting:view-*` policy checks GetReportCatalog
/// already performs, then reused to authorize saved views / favourites against the same rules.
/// </summary>
internal readonly record struct ReportAccessGates(
    bool CanViewRecruitment,
    bool CanViewHr,
    bool CanViewEmployeeStarter,
    bool CanViewLeaveSummary,
    bool CanViewProbation,
    bool CanViewOnboarding,
    bool CanViewWorkloadActions,
    // ADM-08. Defaulted so existing call sites that predate the governance report family keep
    // compiling; the report catalogue endpoint always supplies it explicitly.
    bool CanViewGovernance = false)
{
    public bool IsAuthorized(ReportAccessGate gate) => gate switch
    {
        ReportAccessGate.Recruitment => CanViewRecruitment,
        ReportAccessGate.Hr => CanViewHr,
        ReportAccessGate.EmployeeStarter => CanViewEmployeeStarter,
        ReportAccessGate.LeaveSummary => CanViewLeaveSummary,
        ReportAccessGate.Probation => CanViewProbation,
        ReportAccessGate.Onboarding => CanViewOnboarding,
        ReportAccessGate.WorkloadActions => CanViewWorkloadActions,
        ReportAccessGate.Governance => CanViewGovernance,
        _ => false,
    };
}
