namespace HR.Modules.Companies.Contracts;

public interface ICompanyTimeZoneReader
{
    Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken);
}
