using HR.SharedKernel;

namespace HR.Modules.Leave;

internal sealed record LeaveSubmittedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    string? Reason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave.submitted";
    string IAuditEvent.EntityType => "LeaveRequest";
    Guid IAuditEvent.EntityId => LeaveRequestId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"{TotalDays} day(s) leave submitted";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { LeaveTypeId, StartDate, EndDate, TotalDays, Reason };
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeaveApprovedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    Guid ReviewedByEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave.approved";
    string IAuditEvent.EntityType => "LeaveRequest";
    Guid IAuditEvent.EntityId => LeaveRequestId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ReviewedByEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leave request approved";
    object? IAuditEvent.Before => new { Status = "Pending" };
    object? IAuditEvent.After => new { Status = "Approved" };
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeaveRejectedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    Guid ReviewedByEmployeeId,
    string? RejectionReason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave.rejected";
    string IAuditEvent.EntityType => "LeaveRequest";
    Guid IAuditEvent.EntityId => LeaveRequestId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ReviewedByEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => RejectionReason is null ? "Leave request rejected" : $"Leave request rejected: {RejectionReason}";
    object? IAuditEvent.Before => new { Status = "Pending" };
    object? IAuditEvent.After => new { Status = "Rejected", RejectionReason };
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeaveCancelledAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    string PreviousStatus,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave.cancelled";
    string IAuditEvent.EntityType => "LeaveRequest";
    Guid IAuditEvent.EntityId => LeaveRequestId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leave request cancelled";
    object? IAuditEvent.Before => new { Status = PreviousStatus };
    object? IAuditEvent.After => new { Status = "Cancelled" };
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeaveBalanceAdjustedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid LeaveBalanceId,
    int PolicyYear,
    decimal AdjustmentDays,
    decimal NewRemainingDays,
    Guid AdjustedByEmployeeId,
    DateTimeOffset OccurredAt,
    decimal? AdjustmentHours = null,
    string? Reason = null) : IAuditEvent
{
    string IAuditEvent.EventType => "leave-balance.adjusted";
    string IAuditEvent.EntityType => "LeaveBalance";
    Guid IAuditEvent.EntityId => LeaveBalanceId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => AdjustedByEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => AdjustmentHours.HasValue
        ? $"Leave balance adjusted by {AdjustmentHours.Value} hour(s)" + (Reason is null ? "" : $" ({Reason})")
        : $"Leave balance adjusted by {AdjustmentDays} day(s)";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { AdjustmentDays, AdjustmentHours, NewRemainingDays, PolicyYear, Reason };
    object? IAuditEvent.Metadata => null;
}

internal sealed record ToilAwardedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid TransactionId,
    Guid LeaveBalanceId,
    Guid AwardedByEmployeeId,
    decimal Days,
    DateOnly OccurredOn,
    string? Notes,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "toil.awarded";
    string IAuditEvent.EntityType => "ToilTransaction";
    Guid IAuditEvent.EntityId => TransactionId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => AwardedByEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"{Days} day(s) TOIL awarded";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Days, OccurredOn, Notes };
    object? IAuditEvent.Metadata => null;
}
