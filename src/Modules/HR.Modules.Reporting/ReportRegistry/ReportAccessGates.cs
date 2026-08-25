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
    bool CanViewWorkloadActions)
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
        _ => false,
    };
}
