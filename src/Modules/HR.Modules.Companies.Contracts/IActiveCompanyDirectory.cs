namespace HR.Modules.Companies.Contracts;

public interface IActiveCompanyDirectory
{
    Task<IReadOnlyList<Guid>> GetActiveCompanyIdsAsync(CancellationToken cancellationToken);
}
