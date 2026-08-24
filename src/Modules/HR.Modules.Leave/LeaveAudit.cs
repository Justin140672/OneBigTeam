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

internal sealed record ToilExpiredAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid TransactionId,
    Guid LeaveBalanceId,
    Guid BucketTransactionId,
    decimal Days,
    DateOnly OccurredOn,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "toil.expired";
    string IAuditEvent.EntityType => "ToilTransaction";
    Guid IAuditEvent.EntityId => TransactionId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"{Days} day(s) TOIL expired";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Days, OccurredOn, BucketTransactionId };
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeavePolicyCreatedAuditEvent(
    Guid CompanyId,
    Guid LeavePolicyId,
    string Name,
    decimal CarryOverDays,
    bool AllowNegativeBalance,
    bool RequiresApproval,
    bool IsDefault,
    Guid? ActorEmployeeIdValue,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave-policy.created";
    string IAuditEvent.EntityType => "LeavePolicy";
    Guid IAuditEvent.EntityId => LeavePolicyId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Leave policy '{Name}' created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Name, CarryOverDays, AllowNegativeBalance, RequiresApproval, IsDefault };
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeavePolicyUpdatedAuditEvent(
    Guid CompanyId,
    Guid LeavePolicyId,
    object Before,
    object After,
    Guid? ActorEmployeeIdValue,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave-policy.updated";
    string IAuditEvent.EntityType => "LeavePolicy";
    Guid IAuditEvent.EntityId => LeavePolicyId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leave policy updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeavePolicyAssignedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavePolicyId,
    Guid? PreviousLeavePolicyId,
    DateOnly EffectiveFrom,
    Guid? ActorEmployeeIdValue,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave-policy.assigned";
    string IAuditEvent.EntityType => "EmployeeLeavePolicyAssignment";
    Guid IAuditEvent.EntityId => LeavePolicyId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leave policy assigned to employee";
    object? IAuditEvent.Before => PreviousLeavePolicyId is null ? null : new { LeavePolicyId = PreviousLeavePolicyId };
    object? IAuditEvent.After => new { LeavePolicyId, EffectiveFrom };
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeaveTypeCreatedAuditEvent(
    Guid CompanyId,
    Guid LeaveTypeId,
    string Name,
    string Code,
    decimal DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    Guid? ActorEmployeeIdValue,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave-type.created";
    string IAuditEvent.EntityType => "LeaveType";
    Guid IAuditEvent.EntityId => LeaveTypeId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Leave type '{Name}' created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Name, Code, DefaultEntitlementDays, AccrualMethod, Behaviour };
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeaveTypeUpdatedAuditEvent(
    Guid CompanyId,
    Guid LeaveTypeId,
    object Before,
    object After,
    Guid? ActorEmployeeIdValue,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave-type.updated";
    string IAuditEvent.EntityType => "LeaveType";
    Guid IAuditEvent.EntityId => LeaveTypeId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leave type updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeaveTypeDeactivatedAuditEvent(
    Guid CompanyId,
    Guid LeaveTypeId,
    string Name,
    Guid? ActorEmployeeIdValue,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "leave-type.deactivated";
    string IAuditEvent.EntityType => "LeaveType";
    Guid IAuditEvent.EntityId => LeaveTypeId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeIdValue;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Leave type '{Name}' deactivated";
    object? IAuditEvent.Before => new { IsActive = true };
    object? IAuditEvent.After => new { IsActive = false };
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
