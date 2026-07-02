using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeCompanySicknessSettingsReader(bool excludePublicHolidays = false) : ICompanySicknessSettingsReader
{
    public Task<CompanySicknessSettings> GetSicknessSettingsAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(new CompanySicknessSettings(excludePublicHolidays));
}
