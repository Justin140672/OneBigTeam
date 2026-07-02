namespace HR.SharedKernel;

public interface ICompanySicknessSettingsReader
{
    Task<CompanySicknessSettings> GetSicknessSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
