namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide offboarding progress data for the Offboarding Progress Report (OBT-713), as owned
/// by HR.Modules.Offboarding. One row per employee, using their most-recently-created
/// OffboardingPlan. Distinct from IOffboardingDetailReader (single employee, no task breakdown).
/// </summary>
public interface IOffboardingReportReader
{
    Task<IReadOnlyList<OffboardingReportItem>> GetOffboardingReportAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}

public sealed record OffboardingReportItem(
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    string Status,
    int TotalTasks,
    int CompletedTasks,
    IReadOnlyList<string> OutstandingTaskTitles,
    IReadOnlyList<string> CompletedTaskTitles,
    // True when there is no task titled exactly "Review outstanding documents for employee exit"
    // for this plan, OR that task's Status is Completed. This is the closest existing signal to
    // "documents returned" — Offboarding has no dedicated document-return domain concept; the
    // auto-generated HR review task is what StartOffboarding creates for this purpose.
    bool DocumentsReturned);
