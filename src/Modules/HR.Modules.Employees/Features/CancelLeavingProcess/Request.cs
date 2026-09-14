namespace HR.Modules.Employees.Features.CancelLeavingProcess;

internal sealed record CancelLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    string CancellationReason)
{
    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
