namespace HR.Modules.Employees.Contracts;

public interface IEmploymentTypeReader
{
    Task<bool> IsActiveAsync(Guid companyId, Guid employmentTypeId, CancellationToken cancellationToken);

    Task<string?> GetNameAsync(Guid companyId, Guid employmentTypeId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
