using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ListPublicHolidays;

internal sealed class ListPublicHolidaysHandler
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;

    public ListPublicHolidaysHandler(LeaveDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<ListPublicHolidaysResponse> HandleAsync(
        ListPublicHolidaysRequest request,
        CancellationToken cancellationToken)
    {
        var year = request.Year == 0 ? _clock.UtcNowOffset().Year : request.Year;

        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);

        var items = await _dbContext.PublicHolidays
            .AsNoTracking()
            .Where(h => h.CompanyId == request.CompanyId && h.Date >= start && h.Date <= end)
            .OrderBy(h => h.Date)
            .Select(h => new PublicHolidayItem(h.Id, h.CompanyId, h.Date, h.Name, h.CountryCode, h.CreatedAt))
            .ToListAsync(cancellationToken);

        return new ListPublicHolidaysResponse(items);
    }
}
