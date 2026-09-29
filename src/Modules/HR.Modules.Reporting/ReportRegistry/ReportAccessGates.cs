namespace HR.Modules.Reporting.ReportRegistry;

internal readonly record struct ReportAccessGates(
    bool CanViewRecruitment,
    bool CanViewHr,
    bool CanViewEmployeeStarter,
    bool CanViewLeaveSummary,
    bool CanViewProbation,
    bool CanViewOnboarding,
    bool CanViewWorkloadActions,
    bool CanViewEqualityDiversity)
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
        ReportAccessGate.EqualityDiversity => CanViewEqualityDiversity,
        _ => false,
    };
}
