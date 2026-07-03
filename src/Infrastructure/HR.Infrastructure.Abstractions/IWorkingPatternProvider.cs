namespace HR.Infrastructure.Abstractions;

public interface IWorkingPatternProvider
{
    Task<WorkingPattern> GetEffectivePatternAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
