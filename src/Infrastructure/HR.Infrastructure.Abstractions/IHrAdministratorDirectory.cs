namespace HR.Infrastructure.Abstractions;

public interface IHrAdministratorDirectory
{
    Task<IReadOnlyList<Guid>> GetHrAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
