namespace HR.Modules.Employees.Contracts;

public interface IWorkingPatternProvider
{
    Task<WorkingPattern> GetEffectivePatternAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
