namespace HR.Infrastructure.Abstractions;

public interface ICompanyAdministratorDirectory
{
    Task<IReadOnlyList<Guid>> GetActiveCompanyAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
