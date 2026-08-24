namespace HR.Modules.Companies.Contracts;

public interface IAssetNumberGenerator
{
    // Atomically claims and formats the next asset number for the given company. Backed by a
    // single UPDATE ... RETURNING statement so concurrent callers for the same company each get a
    // distinct number with no read-then-write race and no retry loop required. Mirrors
    // IEmployeeNumberGenerator.GenerateNextAsync.
    Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken);
}
