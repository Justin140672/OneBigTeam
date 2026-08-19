using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CompanyAcknowledgementSettingsReaderTests
{
    [Fact]
    public async Task GetDefaultAcknowledgementStatementAsync_Returns_Configured_Statement_When_Settings_Row_Exists()
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
            NoticePeriodUnit.Months, 1, true, EmployeeNumberMode.Manual, null, 1, 1, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var reader = new CompanyAcknowledgementSettingsReader(context);

        var result = await reader.GetDefaultAcknowledgementStatementAsync(company.Id, CancellationToken.None);

        Assert.Equal("Custom company acknowledgement statement.", result);
    }

    [Fact]
    public async Task GetDefaultAcknowledgementStatementAsync_Returns_Hardcoded_Default_When_No_Settings_Row_Exists()
    {
        await using var context = BuildContext();
        var reader = new CompanyAcknowledgementSettingsReader(context);

        var result = await reader.GetDefaultAcknowledgementStatementAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(CompanySettings.DefaultAcknowledgementStatementText, result);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
