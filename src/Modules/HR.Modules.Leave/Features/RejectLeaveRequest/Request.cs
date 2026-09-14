namespace HR.Modules.Leave.Features.RejectLeaveRequest;

internal sealed record RejectLeaveRequestRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveRequestId { get; init; }
    public Guid ReviewedByEmployeeId { get; init; }
    public string? RejectionReason { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
