namespace HR.Infrastructure.Abstractions;

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
