using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

// Published once an employee's leaving process has been finalised (their leaving date has been
// reached and EmployeeDepartureFinalizer has run) — mirrors EmployeeDepartureFinalisedAuditEvent's
// data, but carries only cross-module-relevant fields for consuming modules (e.g. Leave stops
// generating new policy-year balances/carry-over for the employee from this date forward).
public sealed record EmployeeDepartureFinalisedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LeavingDate,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
