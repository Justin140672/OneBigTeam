using System.Globalization;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetNewHiresTrend;

internal sealed class GetNewHiresTrendHandler(EmployeesDbContext dbContext, IClock clock)
{
    private const int WindowMonths = 6;

    public async Task<GetNewHiresTrendResponse> HandleAsync(
        GetNewHiresTrendRequest request,
        CancellationToken cancellationToken)
    {
        var currentMonthStart = new DateOnly(clock.UtcNow.Year, clock.UtcNow.Month, 1);
        var windowStart = currentMonthStart.AddMonths(-(WindowMonths - 1));
        var windowEndExclusive = currentMonthStart.AddMonths(1);

        var startDates = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId
                     && e.StartDate >= windowStart
                     && e.StartDate < windowEndExclusive)
            .Select(e => e.StartDate)
            .ToListAsync(cancellationToken);

        var counts = startDates
            .GroupBy(d => (d.Year, d.Month))
            .ToDictionary(g => g.Key, g => g.Count());

        var items = new List<NewHiresTrendItem>(WindowMonths);
        for (var i = 0; i < WindowMonths; i++)
        {
            var bucketMonth = windowStart.AddMonths(i);
            var key = (bucketMonth.Year, bucketMonth.Month);
            var monthLabel = bucketMonth.ToDateTime(TimeOnly.MinValue).ToString("MMM yyyy", CultureInfo.InvariantCulture);

            items.Add(new NewHiresTrendItem(
                bucketMonth.Year,
                bucketMonth.Month,
                monthLabel,
                counts.GetValueOrDefault(key, 0)));
        }

        return new GetNewHiresTrendResponse(items);
    }
}
