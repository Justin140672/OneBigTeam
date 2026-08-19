namespace HR.Modules.Companies.Contracts;

public interface ICompanyLeavingSettingsReader
{
    Task<bool> GetAutoDisableAccessOnLeavingDateAsync(Guid companyId, CancellationToken cancellationToken);
}
