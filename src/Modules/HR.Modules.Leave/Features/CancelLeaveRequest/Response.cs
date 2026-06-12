using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.CancelLeaveRequest;

internal sealed record CancelLeaveRequestResponse(
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
    DateTimeOffset UpdatedAt);
