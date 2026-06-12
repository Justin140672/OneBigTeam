namespace HR.SharedKernel;

public interface ICompanyWorkingPatternReader
{
    Task<WorkingPattern?> GetCompanyWorkingPatternAsync(Guid companyId, CancellationToken cancellationToken);
}
