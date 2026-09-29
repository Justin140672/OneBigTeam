namespace HR.Modules.Employees.Contracts;

public interface IEmployeeProbationDatesReader
{
    Task<EmployeeProbationDates?> GetProbationDatesAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}

public sealed record EmployeeProbationDates(DateOnly StartDate, DateOnly? ProbationEndDate);
