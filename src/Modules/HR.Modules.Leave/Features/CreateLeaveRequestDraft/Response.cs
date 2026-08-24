using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.CreateLeaveRequestDraft;

internal sealed record CreateLeaveRequestDraftResponse(
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
    DateTimeOffset CreatedAt);
