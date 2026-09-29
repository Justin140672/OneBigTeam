namespace HR.Infrastructure.Abstractions;

public interface ILeaveSummaryReader
{
    Task<IReadOnlyList<LeaveSummaryReportRow>> GetLeaveSummaryAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        int policyYear,
        CancellationToken cancellationToken);
}

public sealed record LeaveSummaryReportRow(
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    decimal EntitlementDays,
    decimal BookedDays,
    decimal ApprovedDays,
    decimal RemainingDays,
    int PendingRequestCount);
