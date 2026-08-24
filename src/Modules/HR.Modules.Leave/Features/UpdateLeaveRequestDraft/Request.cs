using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.UpdateLeaveRequestDraft;

internal sealed record UpdateLeaveRequestDraftRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveRequestId { get; init; }
    public Guid LeaveTypeId { get; init; }
    public DateOnly StartDate { get; init; }
    public LeaveDayPart StartPart { get; init; }
    public DateOnly EndDate { get; init; }
    public LeaveDayPart EndPart { get; init; }
    public string? Reason { get; init; }
}
