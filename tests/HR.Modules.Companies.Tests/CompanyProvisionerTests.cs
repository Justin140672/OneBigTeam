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

        var companyId = await provisioner.ProvisionCompanyAsync("Acme Corporation", CancellationToken.None);

        var company = await companiesContext.Companies
            .Include(c => c.Addresses)
            .SingleAsync(c => c.Id == companyId);

        // A blank RegisteredOffice address so the admin lands on an editable form (Company Edit's
        // Profile tab) instead of "No addresses found" with nothing to fill in — see
        // CompanyProvisioner.ProvisionCompanyAsync's remarks.
        var address = Assert.Single(company.Addresses);
        Assert.Equal(CompanyAddressType.RegisteredOffice, address.Type);
        Assert.Equal(string.Empty, address.Line1);
        Assert.Null(address.Line2);
        Assert.Equal(string.Empty, address.City);
        Assert.Null(address.Region);
        Assert.Null(address.PostalCode);
        // Always "GB" — UK-only customers for now, no longer user-editable in the UI.
        Assert.Equal("GB", address.CountryCode);
    }

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
