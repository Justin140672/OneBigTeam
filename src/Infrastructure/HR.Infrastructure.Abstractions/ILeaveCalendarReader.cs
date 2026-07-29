namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides individual leave request rows for the Leave Calendar Export Report (OBT-707), as
/// owned by HR.Modules.Leave. Returns unpaged rows for the requested month within a bounded
/// company — callers (Reporting handlers) are responsible for enforcing a maximum row cap on
/// export, same convention as ExportEmployeeDirectoryReport's MaxExportRows.
/// </summary>
public interface ILeaveCalendarReader
{
    Task<IReadOnlyList<LeaveCalendarReportItem>> GetLeaveCalendarAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        int year,
        int month,
        CancellationToken cancellationToken);
}

public sealed record LeaveCalendarReportItem(
    Guid EmployeeId,
    DateOnly LeaveStart,
    DateOnly LeaveEnd,
    string LeaveTypeName,
    decimal DurationDays,
    string ApprovalStatus);
