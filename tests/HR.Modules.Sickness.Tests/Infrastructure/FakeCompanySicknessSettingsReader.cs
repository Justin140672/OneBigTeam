using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeCompanySicknessSettingsReader(
    bool excludePublicHolidays = false,
    int fitNoteRequiredAfterDays = 7,
    int returnToWorkRequiredAfterDays = 1) : ICompanySicknessSettingsReader
{
    public Task<CompanySicknessSettings> GetSicknessSettingsAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(new CompanySicknessSettings(excludePublicHolidays, fitNoteRequiredAfterDays, returnToWorkRequiredAfterDays));
}
