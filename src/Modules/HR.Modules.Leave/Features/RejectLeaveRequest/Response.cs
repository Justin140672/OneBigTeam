using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.RejectLeaveRequest;

internal sealed record RejectLeaveRequestResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    LeaveDayPart StartPart,
    DateOnly EndDate,
    LeaveDayPart EndPart,
    decimal TotalDays,
    string Status,
    Guid ReviewedByEmployeeId,
    DateTimeOffset ReviewedAt,
    string? RejectionReason,
    DateTimeOffset UpdatedAt);
