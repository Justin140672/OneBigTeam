using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CompanyEmployeeNumberSettingsReaderTests
{
    [Fact]
    public async Task GetModeAsync_Returns_Configured_Mode_When_Settings_Row_Exists()
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
            "Custom statement.", 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, "EMP-", 1, 1, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var reader = new CompanyEmployeeNumberSettingsReader(context);

        var mode = await reader.GetModeAsync(company.Id, CancellationToken.None);

        Assert.Equal(EmployeeNumberMode.Automatic, mode);
    }

    [Fact]
    public async Task GetModeAsync_Returns_Manual_When_No_Settings_Row_Exists()
    {
        await using var context = BuildContext();
        var reader = new CompanyEmployeeNumberSettingsReader(context);

        var mode = await reader.GetModeAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(EmployeeNumberMode.Automatic, mode);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
