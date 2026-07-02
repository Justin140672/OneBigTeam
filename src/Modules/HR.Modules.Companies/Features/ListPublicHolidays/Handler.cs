using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.ListPublicHolidays;

internal sealed class ListPublicHolidaysHandler(CompaniesDbContext dbContext)
{
    private readonly CompaniesDbContext _dbContext = dbContext;

    public async Task<ListPublicHolidaysResponse> HandleAsync(
        ListPublicHolidaysRequest request,
        CancellationToken cancellationToken)
    {
        var items = await _dbContext.PublicHolidays
            .AsNoTracking()
            .Where(h => h.CompanyId == request.CompanyId)
            .OrderBy(h => h.Date)
            .Select(h => new PublicHolidayItem(h.Id, h.CompanyId, h.Date, h.Name, h.CountryCode, h.CreatedAt))
            .ToListAsync(cancellationToken);

        return new ListPublicHolidaysResponse(items);
    }
}
