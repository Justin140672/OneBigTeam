using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class PublicHolidayReader(CompaniesDbContext dbContext) : IPublicHolidayReader
{
    public async Task<IReadOnlyCollection<PublicHolidayDate>> GetPublicHolidaysAsync(
        Guid companyId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        return await dbContext.PublicHolidays
            .AsNoTracking()
            .Where(h => h.CompanyId == companyId && h.Date >= from && h.Date <= to)
            .Select(h => new PublicHolidayDate(h.Date, h.Name))
            .ToListAsync(cancellationToken);
    }
}
