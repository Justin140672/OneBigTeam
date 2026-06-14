using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetCompanySettings;

internal sealed class GetCompanySettingsHandler(CompaniesDbContext dbContext)
{
    public async Task<Result<GetCompanySettingsResponse>> HandleAsync(
        GetCompanySettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == request.Id, cancellationToken);

        if (settings is null)
        {
            // Company exists but has no customised settings — return defaults
            var exists = await dbContext.Companies
                .AsNoTracking()
                .AnyAsync(c => c.Id == request.Id, cancellationToken);

            if (!exists)
                return Result.Failure<GetCompanySettingsResponse>(
                    Error.NotFound($"Company with id '{request.Id}' was not found."));

            var defaults = Domain.CompanySettings.CreateDefault(request.Id, DateTimeOffset.UtcNow);
            return Result.Success(new GetCompanySettingsResponse(
                defaults.CompanyId, defaults.TimeZone, defaults.Locale,
                (int)defaults.WorkingDays, defaults.HoursPerDay, defaults.LeaveYearStartMonth,
                defaults.DefaultHolidayAllowance, defaults.ProbationMonths,
                defaults.ExcludePublicHolidaysFromLeave, defaults.UpdatedAt));
        }

        return Result.Success(new GetCompanySettingsResponse(
            settings.CompanyId, settings.TimeZone, settings.Locale,
            (int)settings.WorkingDays, settings.HoursPerDay, settings.LeaveYearStartMonth,
            settings.DefaultHolidayAllowance, settings.ProbationMonths,
            settings.ExcludePublicHolidaysFromLeave, settings.UpdatedAt));
    }
}
