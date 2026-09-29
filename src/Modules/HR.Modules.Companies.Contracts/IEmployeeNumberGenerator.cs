namespace HR.Modules.Companies.Contracts;

public interface IEmployeeNumberGenerator
{
    Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken);
}
