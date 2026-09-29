namespace HR.Modules.Companies.Contracts;

public interface IAssetNumberGenerator
{
    Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken);
}
