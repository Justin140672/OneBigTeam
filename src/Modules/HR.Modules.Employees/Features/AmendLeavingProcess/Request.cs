using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.AmendLeavingProcess;

internal sealed record AmendLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    LeavingReason LeavingReason,
    bool ConfirmBackdatedLeavingDate = false,
    // Ticket 2 (optimistic concurrency) — see UpdateEmployeeProfileRequest.ExpectedVersion.
    int? ExpectedVersion = null);
