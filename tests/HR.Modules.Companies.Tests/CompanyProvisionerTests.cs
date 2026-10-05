using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class CompanyProvisionerTests
{
    [Fact]
    public async Task ProvisionCompanyAsync_Creates_Company_With_Blank_RegisteredOffice_Address()
    {
        await using var companiesContext = BuildContext();
        await using var platformContext = BuildPlatformContext();
        var provisioner = new CompanyProvisioner(
            companiesContext,
            platformContext,
            new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)),
            new ConfigurationBuilder().Build());

        var companyId = await provisioner.ProvisionCompanyAsync("Acme Corporation", Admin("jane.smith@acme.example"), CancellationToken.None);

        var company = await companiesContext.Companies
            .Include(c => c.Addresses)
            .SingleAsync(c => c.Id == companyId);

        var address = Assert.Single(company.Addresses);
        Assert.Equal(CompanyAddressType.RegisteredOffice, address.Type);
        Assert.Equal(string.Empty, address.Line1);
        Assert.Null(address.Line2);
        Assert.Equal(string.Empty, address.City);
        Assert.Null(address.Region);
        Assert.Null(address.PostalCode);
        Assert.Equal("GB", address.CountryCode);
    }

    [Fact]
    public async Task ProvisionCompanyAsync_Scaffolds_Only_Public_Holidays_After_Today()
    {
        await using var companiesContext = BuildContext();
        await using var platformContext = BuildPlatformContext();
        var provisioner = new CompanyProvisioner(
            companiesContext,
            platformContext,
            new FakeClock(new DateTime(2026, 12, 25, 10, 0, 0, DateTimeKind.Utc)),
            new ConfigurationBuilder().Build());

        var companyId = await provisioner.ProvisionCompanyAsync("Acme Corporation", Admin("jane.smith@acme.example"), CancellationToken.None);

        var holidays = await companiesContext.PublicHolidays
            .Where(h => h.CompanyId == companyId)
            .OrderBy(h => h.Date)
            .ToListAsync();

        Assert.NotEmpty(holidays);
        Assert.All(holidays, h => Assert.True(h.Date > new DateOnly(2026, 12, 25)));
        Assert.All(holidays, h => Assert.Equal("GB", h.CountryCode));
        Assert.Equal(new DateOnly(2026, 12, 28), holidays[0].Date);
        Assert.Equal(new DateOnly(2027, 12, 28), holidays[^1].Date);
    }

    [Fact]
    public async Task PublicHolidayScaffolder_Does_Not_Duplicate_When_Run_Twice()
    {
        await using var companiesContext = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

        await PublicHolidayScaffolder.AddUpcomingAsync(companiesContext, companyId, now, CancellationToken.None);
        await companiesContext.SaveChangesAsync();
        var first = await companiesContext.PublicHolidays.CountAsync(h => h.CompanyId == companyId);

        await PublicHolidayScaffolder.AddUpcomingAsync(companiesContext, companyId, now, CancellationToken.None);
        await companiesContext.SaveChangesAsync();
        var second = await companiesContext.PublicHolidays.CountAsync(h => h.CompanyId == companyId);

        Assert.Equal(10, first);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("Jane.Smith@Acme.Example", "Jane", "Smith", "acme.example", WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData("j.smith@acme.co.uk", "Jane", "Smith", "acme.co.uk", WorkEmailNamingConvention.FirstInitialDotLastName)]
    [InlineData("janesmith@acme.co.uk", "Jane", "Smith", "acme.co.uk", WorkEmailNamingConvention.FirstNameLastName)]
    [InlineData("jane@acme.co.uk", "Jane", "Smith", "acme.co.uk", WorkEmailNamingConvention.FirstName)]
    [InlineData("admin@acme.co.uk", "Jane", "Smith", "acme.co.uk", WorkEmailNamingConvention.FirstNameDotLastName)]
    public async Task ProvisionCompanyAsync_Sets_Mandatory_WorkEmail_Settings_From_The_First_User(
        string email, string firstName, string lastName, string expectedDomain, WorkEmailNamingConvention expectedConvention)
    {
        await using var companiesContext = BuildContext();
        await using var platformContext = BuildPlatformContext();
        var provisioner = new CompanyProvisioner(
            companiesContext,
            platformContext,
            new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)),
            new ConfigurationBuilder().Build());

        var companyId = await provisioner.ProvisionCompanyAsync(
            "Acme Corporation", new CompanyProvisioningAdmin(email, firstName, lastName), CancellationToken.None);

        var settings = await companiesContext.CompanySettings.SingleAsync(s => s.CompanyId == companyId);
        Assert.True(settings.WorkEmailSuggestionsEnabled);
        Assert.Equal(expectedDomain, settings.WorkEmailPrimaryDomain);
        Assert.Equal(expectedConvention, settings.WorkEmailNamingConvention);
    }

    [Theory]
    [InlineData("jane.smith")]
    [InlineData("jane.smith@localhost")]
    [InlineData("")]
    public async Task ProvisionCompanyAsync_Throws_And_Persists_Nothing_When_Email_Has_No_Valid_Domain(string email)
    {
        await using var companiesContext = BuildContext();
        await using var platformContext = BuildPlatformContext();
        var provisioner = new CompanyProvisioner(
            companiesContext,
            platformContext,
            new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)),
            new ConfigurationBuilder().Build());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.ProvisionCompanyAsync("Acme Corporation", Admin(email), CancellationToken.None));

        Assert.Empty(await companiesContext.Companies.ToListAsync());
    }

    private static CompanyProvisioningAdmin Admin(string email) => new(email, "Jane", "Smith");

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CompaniesDbContext(options);
    }

    private static PlatformDbContext BuildPlatformContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PlatformDbContext(options);
    }
}
