using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CompanyNoticePeriodSettingsReaderTests
{
    [Fact]
    public async Task GetDefaultNoticePeriodAsync_Returns_Configured_Values_When_Settings_Row_Exists()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, 7, 1,
            "Custom company acknowledgement statement.", 3,
            NoticePeriodUnit.Weeks, 4, false, EmployeeNumberMode.Manual, null, 1, 1, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var reader = new CompanyNoticePeriodSettingsReader(context);

        var (unit, length) = await reader.GetDefaultNoticePeriodAsync(company.Id, CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Weeks, unit);
        Assert.Equal(4, length);
    }

    [Fact]
    public async Task GetDefaultNoticePeriodAsync_Returns_Fallback_When_No_Settings_Row_Exists()
    {
        await using var context = BuildContext();
        var reader = new CompanyNoticePeriodSettingsReader(context);

        var (unit, length) = await reader.GetDefaultNoticePeriodAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Months, unit);
        Assert.Equal(1, length);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
