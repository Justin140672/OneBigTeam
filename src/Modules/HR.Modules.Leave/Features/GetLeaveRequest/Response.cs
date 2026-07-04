namespace HR.Modules.Leave.Features.GetLeaveRequest;

internal sealed record GetLeaveRequestResponse(
    Guid Id,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string Status,
    DateOnly StartDate,
    string StartPart,
    DateOnly EndDate,
    string EndPart,
    decimal TotalDays,
    string? Reason,
    string? RejectionReason,
    DateTimeOffset CreatedAt);
