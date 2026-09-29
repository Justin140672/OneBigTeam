using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.StartLeavingProcess;

internal sealed record StartLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    LeavingReason LeavingReason,
    bool ConfirmBackdatedLeavingDate = false,
    Guid? ReplacementManagerEmployeeId = null,
    string? Notes = null)
{
    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
