using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

// Published once an employee's leaving process has been finalised (their leaving date has been
// reached and EmployeeDepartureFinalizer has run) — mirrors EmployeeDepartureFinalisedAuditEvent's
// data, but carries only cross-module-relevant fields for consuming modules (e.g. Leave stops
// generating new policy-year balances/carry-over for the employee from this date forward).
//
// AccessDisabled is the authoritative, already-evaluated decision (Employee.SetSystemAccess plus
// the company's auto-disable-on-leaving-date setting — see EmployeeDepartureFinalizer) about
// whether this employee's application account should be disabled as a direct result of departure.
// Identity's OnEmployeeDepartureFinalised handler is the only consumer that acts on this flag —
// it must never re-derive the auto-disable policy itself, since Employees already owns that
// decision and Identity must not query Companies settings directly.
public sealed record EmployeeDepartureFinalisedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LeavingDate,
    DateTimeOffset OccurredAt,
    bool AccessDisabled) : IIntegrationEvent;
