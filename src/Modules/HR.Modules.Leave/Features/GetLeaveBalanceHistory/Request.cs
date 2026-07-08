namespace HR.Modules.Leave.Features.GetLeaveBalanceHistory;

internal sealed record GetLeaveBalanceHistoryRequest(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId);
