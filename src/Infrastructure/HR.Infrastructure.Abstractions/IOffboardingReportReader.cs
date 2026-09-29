namespace HR.Infrastructure.Abstractions;

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
    bool DocumentsReturned,
    IReadOnlyList<Guid>? OutstandingTaskIds = null);
