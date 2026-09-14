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
    // OFF-06: manager HR nominates to take over this employee's direct reports (and any of their
    // own pending manager-scoped approvals/reviews) once their departure is finalised. Optional —
    // only meaningful when the departing employee actually has direct reports; ignored otherwise.
    // When omitted for a manager with direct reports, those reports are left without a manager
    // and the departure is routed to an HR exception queue.
    Guid? ReplacementManagerEmployeeId = null)
{
    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
