using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal static class StagingCompanySeeder
{
    private const int TrialLengthDays = 14;

    public static async Task SeedAsync(CompaniesDbContext db, StagingSeedOptions options, DateTimeOffset now)
    {
        var companyId = StagingSeedOptions.CompanyId;

        var existing = await db.Companies
            .Include(c => c.Settings)
            .SingleOrDefaultAsync(c => c.Id == companyId);

        if (existing is not null)
        {
            if (existing.Settings is { WorkEmailPrimaryDomain: null } existingSettings)
            {
                ApplyWorkEmailSettings(existingSettings, options, now);
                await db.SaveChangesAsync();
            }

            return;
        }

        var company = Company.Create(companyId, options.ResolvedCompanyName, now);

        var settings = CompanySettings.CreateDefault(companyId, now);
        settings.UpdateHrPolicy(
            settings.WorkingDays,
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
            EmployeeNumberMode.Automatic,
            StagingSeedOptions.EmployeeNumberPrefix,
            StagingSeedOptions.NextEmployeeNumber,
            StagingSeedOptions.EmployeeNumberMinimumLength,
            now);
        ApplyWorkEmailSettings(settings, options, now);
        company.SetSettings(settings, now);

        company.SetAddress(
            CompanyAddress.Create(Guid.NewGuid(), companyId, CompanyAddressType.RegisteredOffice,
                "1 Finsbury Avenue", null, "London", null, "EC2M 2PF", "GB", now),
            now);

        company.Activate(now);

        db.Companies.Add(company);
        db.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, now, TrialLengthDays));

        await PublicHolidayScaffolder.AddUpcomingAsync(db, companyId, now, CancellationToken.None);

        await db.SaveChangesAsync();
    }

    private static void ApplyWorkEmailSettings(CompanySettings settings, StagingSeedOptions options, DateTimeOffset now) =>
        settings.UpdateWorkEmailSettings(
            suggestionsEnabled: true,
            options.ResolvedEmailDomain,
            additionalDomains: null,
            WorkEmailNamingConvention.FirstNameDotLastName,
            now);
}
