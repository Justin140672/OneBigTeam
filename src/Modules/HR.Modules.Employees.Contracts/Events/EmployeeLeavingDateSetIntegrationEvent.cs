using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

// Published whenever an employee's leaving date is set (StartLeavingProcessHandler) or amended
// (AmendLeavingProcessHandler) — i.e. whenever LeavingDate is written for an in-progress leaving
// process. Consuming modules (e.g. Leave, LEAVE-05) use this to recalculate the employee's
// current policy year entitlement pro-rated through LeavingDate. A single event type deliberately
// covers both "set" and "amend": consumers must always recalculate from the given LeavingDate
// (never apply it as an incremental delta), so a chain of Start -> Amend -> Amend converges on the
// entitlement implied by the current leaving date rather than compounding reductions.
//
// This is distinct from EmployeeDepartureFinalisedIntegrationEvent, which only fires once the
// leaving process is finalised (the leaving date has actually been reached/confirmed) — this event
// fires immediately when the leaving date is set or amended, while the process is still in
// progress and may yet be cancelled.
public sealed record EmployeeLeavingDateSetIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LeavingDate,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
