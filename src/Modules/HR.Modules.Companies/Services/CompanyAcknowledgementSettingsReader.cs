using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyAcknowledgementSettingsReader(CompaniesDbContext dbContext) : ICompanyAcknowledgementSettingsReader
{
    public async Task<string> GetDefaultAcknowledgementStatementAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var statement = await dbContext.CompanySettings
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => s.DefaultAcknowledgementStatement)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(statement)
            ? CompanySettings.DefaultAcknowledgementStatementText
            : statement;
    }

    public async Task<int> GetReminderIntervalDaysAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var interval = await dbContext.CompanySettings
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => (int?)s.AcknowledgementReminderIntervalDays)
            .SingleOrDefaultAsync(cancellationToken);

        return interval ?? 3;
    }
}
