using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompany;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanyHandlerTests
{
    [Fact]
    public async Task HandleAsync_Updates_Name_And_Addresses()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var existing = CompanyAddress.Create(
            Guid.NewGuid(), company.Id, CompanyAddressType.RegisteredOffice,
            "1 Old Street", null, "London", null, "EC1", "GB", now);
        company.SetAddress(existing, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanyHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(new UpdateCompanyRequest
        {
            CompanyId = company.Id,
            Name = "Acme Corporation",
            Addresses =
            [
                new UpdateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "10 High Street",
                    City = "London",
                    PostalCode = "SW1A 1AA",
                    CountryCode = "GB",
                }
            ]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme Corporation", result.Value!.Name);
        Assert.Equal(2, result.Value.Addresses.Count);

        var saved = await context.Companies
            .Include(c => c.Addresses)
            .SingleAsync(c => c.Id == company.Id);
        Assert.Equal("Acme Corporation", saved.Name);
        Assert.Equal(2, saved.Addresses.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateCompanyHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(new UpdateCompanyRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Unknown",
            Addresses =
            [
                new UpdateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "1 Main St",
                    City = "London",
                    CountryCode = "GB",
                }
            ]
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PostalCode_Does_Not_Match_Company_Regex()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanyHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(new UpdateCompanyRequest
        {
            CompanyId = company.Id,
            Name = "Acme Corporation",
            Addresses =
            [
                new UpdateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "10 High Street",
                    City = "London",
                    PostalCode = "not a postcode",
                    CountryCode = "GB",
                }
            ]
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        var saved = await context.Companies.SingleAsync(c => c.Id == company.Id);
        Assert.Equal("Acme", saved.Name); // unchanged — validation ran before any updates were applied
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Default_Uk_Regex_When_Company_Has_No_Settings()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now); // no SetSettings call
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanyHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(new UpdateCompanyRequest
        {
            CompanyId = company.Id,
            Name = "Acme Corporation",
            Addresses =
            [
                new UpdateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "10 High Street",
                    City = "London",
                    PostalCode = "SW1A 1AA",
                    CountryCode = "GB",
                }
            ]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
