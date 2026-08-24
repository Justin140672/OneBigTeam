namespace HR.Modules.Employees.Contracts;

// PROB-06: purpose-specific read contract so the Probation module can create a deferred probation
// record once a manager is assigned after employee creation (EmployeeManagerChangedIntegrationEvent
// carries no date fields), without querying the Employees module's tables directly. Mirrors the
// existing IEmployeeStartDateReader pattern.
public interface IEmployeeProbationDatesReader
{
    Task<EmployeeProbationDates?> GetProbationDatesAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}

public sealed record EmployeeProbationDates(DateOnly StartDate, DateOnly? ProbationEndDate);
