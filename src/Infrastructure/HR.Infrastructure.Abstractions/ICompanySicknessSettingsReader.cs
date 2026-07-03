namespace HR.Infrastructure.Abstractions;

public interface ICompanySicknessSettingsReader
{
    Task<CompanySicknessSettings> GetSicknessSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
