namespace HR.Modules.Leave.Features.AdjustLeaveBalance;

internal sealed record AdjustLeaveBalanceResponse(
    Guid AdjustmentId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid LeaveBalanceId,
    decimal AdjustmentDays,
    decimal? AdjustmentHours,
    decimal NewRemainingDays,
    decimal NewRemainingHours,
    string Reason,
    string? Comments,
    Guid AdjustedByEmployeeId,
    DateTimeOffset AdjustedAt);
