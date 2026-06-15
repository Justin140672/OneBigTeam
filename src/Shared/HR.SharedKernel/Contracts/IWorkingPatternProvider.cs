namespace HR.SharedKernel;

public interface IWorkingPatternProvider
{
    Task<WorkingPattern> GetEffectivePatternAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
