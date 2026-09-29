using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal static class PublicHolidayScaffolder
{
    private const int YearsAhead = 1;

    public static async Task AddUpcomingAsync(
        CompaniesDbContext dbContext,
        Guid companyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var existingDates = (await dbContext.PublicHolidays
                .AsNoTracking()
                .Where(h => h.CompanyId == companyId)
                .Select(h => h.Date)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var (date, name) in UkPublicHolidayCalendar.GetUpcoming(today, YearsAhead))
        {
            if (!existingDates.Add(date))
            {
                continue;
            }

            dbContext.PublicHolidays.Add(PublicHoliday.Create(
                Guid.NewGuid(), companyId, date, name, UkPublicHolidayCalendar.CountryCode, now));
        }
    }
}
