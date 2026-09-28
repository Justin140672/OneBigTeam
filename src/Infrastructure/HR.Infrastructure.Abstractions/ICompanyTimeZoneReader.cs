namespace HR.Infrastructure.Abstractions;

public interface ICompanyTimeZoneReader
{
    Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken);
}
