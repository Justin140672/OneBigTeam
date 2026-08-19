namespace HR.Modules.Companies.Contracts;

public interface ICompanySicknessSettingsReader
{
    Task<CompanySicknessSettings> GetSicknessSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
