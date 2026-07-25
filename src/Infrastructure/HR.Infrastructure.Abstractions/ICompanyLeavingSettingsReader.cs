namespace HR.Infrastructure.Abstractions;

public interface ICompanyLeavingSettingsReader
{
    Task<bool> GetAutoDisableAccessOnLeavingDateAsync(Guid companyId, CancellationToken cancellationToken);
}
