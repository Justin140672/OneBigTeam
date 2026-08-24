using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyProbationSettingsReader(CompaniesDbContext dbContext) : ICompanyProbationSettingsReader
{
    private const int DefaultProbationMonths = 6;

    public async Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings?.ProbationMonths ?? DefaultProbationMonths;
    }

    public async Task<IReadOnlyList<int>> GetCheckpointDaysAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
            return CompanyProbationSettings.DefaultCheckpointDays;

        var configured = new[]
            {
                settings.ProbationCheckpointDay1,
                settings.ProbationCheckpointDay2,
                settings.ProbationCheckpointDay3
            }
            .Where(day => day is > 0)
            .Select(day => day!.Value)
            .Distinct()
            .OrderBy(day => day)
            .ToList();

        return configured.Count == 0
            ? CompanyProbationSettings.DefaultCheckpointDays
            : configured;
    }
}
