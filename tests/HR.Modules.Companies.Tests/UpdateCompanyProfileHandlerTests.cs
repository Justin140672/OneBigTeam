using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompanyProfile;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanyProfileHandlerTests
{
    [Fact]
    public async Task HandleAsync_Updates_Company_Name_And_Addresses()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", "acme", now);
        var existingAddress = CompanyAddress.Create(
            Guid.NewGuid(),
            company.Id,
            CompanyAddressType.RegisteredOffice,
            "1 Old Street",
            null,
            "London",
            null,
            "EC1",
            "GB",
            now);
        company.SetAddress(existingAddress, now);

        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanyProfileHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new UpdateCompanyProfileRequest
            {
                Id = company.Id,
                Name = "Acme Corporation",
                Addresses =
                [
                    new UpdateCompanyAddressRequest
                    {
                        Type = CompanyAddressType.RegisteredOffice,
                        Line1 = "10 High Street",
                        City = "London",
                        PostalCode = "SW1A 1AA",
                        CountryCode = "GB"
                    }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Acme Corporation", result.Value!.Name);
        Assert.Equal(2, result.Value.Addresses.Count);

        var savedCompany = await context.Companies
            .Include(currentCompany => currentCompany.Addresses)
            .SingleAsync(currentCompany => currentCompany.Id == company.Id);

        Assert.Equal("Acme Corporation", savedCompany.Name);
        Assert.Equal(2, savedCompany.Addresses.Count);
        Assert.Equal("10 High Street", savedCompany.Addresses.Single(a => a.Type == CompanyAddressType.RegisteredOffice).Line1);
        Assert.Equal("London", savedCompany.Addresses.Single(a => a.Type == CompanyAddressType.TradingAddress).City);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateCompanyProfileHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new UpdateCompanyProfileRequest
            {
                Id = Guid.NewGuid(),
                Name = "Unknown"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
