using HR.Modules.Companies.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetHrSettings;

internal sealed class GetHrSettingsHandler(CompaniesDbContext dbContext)
{
    public async Task<Result<GetHrSettingsResponse>> HandleAsync(
        GetHrSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (settings is null)
        {
            // Company exists but has no customised settings — return defaults
            var exists = await dbContext.Companies
                .AsNoTracking()
                .AnyAsync(c => c.Id == request.CompanyId, cancellationToken);

            if (!exists)
                return Result.Failure<GetHrSettingsResponse>(
                    Error.NotFound($"Company with id '{request.CompanyId}' was not found."));

            var defaults = Domain.CompanySettings.CreateDefault(request.CompanyId, DateTimeOffset.UtcNow);
            return Result.Success(new GetHrSettingsResponse(
                defaults.CompanyId,
                (int)defaults.WorkingDays,
                defaults.HoursPerDay,
                defaults.LeaveYearStartMonth,
                defaults.DefaultHolidayAllowance,
                defaults.ProbationMonths,
                defaults.ExcludePublicHolidaysFromLeave,
                defaults.ExcludePublicHolidaysFromSickness,
                defaults.DisplaySalaryOnEmployeeProfile,
                defaults.FitNoteRequiredAfterDays,
                defaults.ReturnToWorkRequiredAfterDays,
                defaults.DefaultAcknowledgementStatement,
                defaults.AcknowledgementReminderIntervalDays,
                defaults.NoticePeriodUnit,
                defaults.NoticePeriodLength,
                defaults.AutoDisableAccessOnLeavingDate,
                defaults.EmployeeNumberMode,
                defaults.EmployeeNumberPrefix,
                defaults.NextEmployeeNumber,
                defaults.EmployeeNumberMinimumLength,
                defaults.UpdatedAt));
        }

        return Result.Success(new GetHrSettingsResponse(
            settings.CompanyId,
            (int)settings.WorkingDays,
            settings.HoursPerDay,
            settings.LeaveYearStartMonth,
            settings.DefaultHolidayAllowance,
            settings.ProbationMonths,
            settings.ExcludePublicHolidaysFromLeave,
            settings.ExcludePublicHolidaysFromSickness,
            settings.DisplaySalaryOnEmployeeProfile,
            settings.FitNoteRequiredAfterDays,
            settings.ReturnToWorkRequiredAfterDays,
            settings.DefaultAcknowledgementStatement,
            settings.AcknowledgementReminderIntervalDays,
            settings.NoticePeriodUnit,
            settings.NoticePeriodLength,
            settings.AutoDisableAccessOnLeavingDate,
            settings.EmployeeNumberMode,
            settings.EmployeeNumberPrefix,
            settings.NextEmployeeNumber,
            settings.EmployeeNumberMinimumLength,
            settings.UpdatedAt));
    }
}
