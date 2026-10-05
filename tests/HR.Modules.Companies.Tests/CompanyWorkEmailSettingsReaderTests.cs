using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CompanyWorkEmailSettingsReaderTests
{
    [Fact]
    public async Task GetWorkEmailSettingsAsync_Returns_Defaults_When_No_Settings_Row_Exists()
    {
        await using var context = BuildContext();
        var reader = new CompanyWorkEmailSettingsReader(context);

        var settings = await reader.GetWorkEmailSettingsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(CompanyWorkEmailSettings.Default, settings);
        Assert.True(settings.SuggestionsEnabled);
        Assert.Null(settings.PrimaryDomain);
        Assert.Empty(settings.AllDomains);
        Assert.Equal(WorkEmailNamingConvention.FirstNameDotLastName, settings.NamingConvention);
    }

    [Fact]
    public async Task GetWorkEmailSettingsAsync_Returns_Configured_Values_With_Primary_Domain_First()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var companySettings = CompanySettings.CreateDefault(company.Id, now);
        companySettings.UpdateWorkEmailSettings(true, "example.com", ["alt.com"], WorkEmailNamingConvention.FirstName, now);
        company.SetSettings(companySettings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var settings = await new CompanyWorkEmailSettingsReader(context)
            .GetWorkEmailSettingsAsync(company.Id, CancellationToken.None);

        Assert.True(settings.SuggestionsEnabled);
        Assert.Equal("example.com", settings.PrimaryDomain);
        Assert.Equal(["alt.com"], settings.AdditionalDomains);
        Assert.Equal(["example.com", "alt.com"], settings.AllDomains);
        Assert.Equal(WorkEmailNamingConvention.FirstName, settings.NamingConvention);
    }

    [Fact]
    public async Task GetWorkEmailSettingsAsync_Does_Not_Return_Another_Companys_Settings()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var companySettings = CompanySettings.CreateDefault(company.Id, now);
        companySettings.UpdateWorkEmailSettings(true, "example.com", [], WorkEmailNamingConvention.FirstName, now);
        company.SetSettings(companySettings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var settings = await new CompanyWorkEmailSettingsReader(context)
            .GetWorkEmailSettingsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(settings.PrimaryDomain);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
