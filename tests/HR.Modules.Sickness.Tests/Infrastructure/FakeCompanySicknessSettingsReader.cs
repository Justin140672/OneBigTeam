using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeCompanySicknessSettingsReader(
    bool excludePublicHolidays = false,
    int? fitNoteRequiredAfterDays = null,
    int? returnToWorkRequiredAfterDays = null) : ICompanySicknessSettingsReader
{
    public Task<CompanySicknessSettings> GetSicknessSettingsAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(new CompanySicknessSettings(excludePublicHolidays, fitNoteRequiredAfterDays, returnToWorkRequiredAfterDays));
}
