namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides per-employee, per-leave-type leave summary rows (entitlement/booked/approved/
/// remaining/pending) for the Leave Summary Report (OBT-706), as owned by HR.Modules.Leave.
/// Returns unaggregated rows for the requesting company (optionally narrowed to a set of
/// employees for manager-scoped/row-level-filtered callers) — grouping/aggregation by
/// employee/department/leave-type is performed by the HR.Modules.Reporting handler, since
/// department is not data Leave owns.
/// </summary>
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
