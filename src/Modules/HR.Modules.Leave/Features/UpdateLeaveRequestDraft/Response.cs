using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.UpdateLeaveRequestDraft;

internal sealed record UpdateLeaveRequestDraftResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid? LeavePolicyId,
    string Status,
    DateOnly StartDate,
    LeaveDayPart StartPart,
    DateOnly EndDate,
    LeaveDayPart EndPart,
    decimal TotalDays,
    string? Reason,
    DateTimeOffset UpdatedAt);
