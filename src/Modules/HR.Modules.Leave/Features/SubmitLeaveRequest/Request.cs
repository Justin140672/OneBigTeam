using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.SubmitLeaveRequest;

internal sealed record SubmitLeaveRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveTypeId { get; init; }
    public DateOnly StartDate { get; init; }
    public LeaveDayPart StartPart { get; init; }
    public DateOnly EndDate { get; init; }
    public LeaveDayPart EndPart { get; init; }
    public string? Reason { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
