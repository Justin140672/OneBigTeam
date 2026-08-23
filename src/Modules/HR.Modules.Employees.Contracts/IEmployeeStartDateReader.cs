namespace HR.Modules.Employees.Contracts;

// Purpose-specific read contract so other modules (e.g. Leave, reacting to
// EmployeeDetailsCorrectedIntegrationEvent to recalculate current-year leave entitlement) can
// obtain an employee's current start date without querying the Employees module's tables
// directly. EmployeeDetailsCorrectedIntegrationEvent is deliberately generic and carries no
// field-level payload, so consumers that need the current value of a specific field use a
// dedicated reader like this one instead.
public interface IEmployeeStartDateReader
{
    Task<DateOnly?> GetStartDateAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
