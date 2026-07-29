namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide (optionally employee-id-filtered) onboarding progress data for the Onboarding
/// Progress Report (OBT-712), as owned by HR.Modules.Onboarding. Distinct from
/// IOnboardingStatusReader, which only reads a single employee's latest onboarding status string
/// and is not sufficient for a company-wide report. When employeeIds is supplied, results are
/// restricted to those employees only — used by the Reporting module to apply row-level manager
/// scoping without this reader knowing anything about callers/roles.
/// </summary>
public interface IOnboardingReportReader
{
    Task<IReadOnlyList<OnboardingReportItem>> GetOnboardingReportAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        CancellationToken cancellationToken);
}

public sealed record OnboardingReportItem(
    Guid EmployeeId,
    Guid PlanId,
    string PlanStatus,
    DateOnly StartDate,
    int TotalTasks,
    int CompletedTasks,
    IReadOnlyList<OnboardingReportTaskItem> OutstandingTasks);

public sealed record OnboardingReportTaskItem(
    string Title,
    DateOnly? DueDate,
    string Owner,
    bool IsOverdue);
