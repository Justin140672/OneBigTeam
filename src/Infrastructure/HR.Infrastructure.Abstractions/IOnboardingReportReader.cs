namespace HR.Infrastructure.Abstractions;

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
    bool IsOverdue,
    Guid TaskId = default);
