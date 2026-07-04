using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.CreateCompany;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CreateCompanyHandlerTests
{
    [Fact]
    public async Task HandleAsync_Creates_Company()
    {
        await using var context = BuildContext();
        var handler = new CreateCompanyHandler(context, new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new CreateCompanyRequest { Name = "Acme Corporation" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Acme Corporation", result.Value!.Name);
        Assert.Empty(result.Value.Addresses);

        var company = await context.Companies.SingleAsync();
        Assert.Equal(result.Value.Id, company.Id);
        Assert.Equal("Acme Corporation", company.Name);
        Assert.True(company.IsActive);

        var settings = await context.CompanySettings.SingleAsync();
        Assert.Equal(company.Id, settings.CompanyId);
        Assert.Equal("UTC", settings.TimeZone);
        Assert.Equal("en-GB", settings.Locale);
        Assert.Equal(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                     WorkingDays.Thursday | WorkingDays.Friday, settings.WorkingDays);
        Assert.Equal(7.5m, settings.HoursPerDay);
        Assert.Equal(1, settings.LeaveYearStartMonth);
        Assert.Equal(25, settings.DefaultHolidayAllowance);
        Assert.Equal(6, settings.ProbationMonths);
    }

    [Fact]
    public async Task HandleAsync_Creates_Company_With_Addresses()
    {
        await using var context = BuildContext();
        var handler = new CreateCompanyHandler(context, new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new CreateCompanyRequest
            {
                Name = "Acme Corporation",
                Addresses =
                [
                    new CreateCompanyAddressRequest
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
        Assert.Equal(2, result.Value!.Addresses.Count);
        Assert.Contains(result.Value.Addresses, address => address.Type == CompanyAddressType.RegisteredOffice);
        Assert.Contains(result.Value.Addresses, address => address.Type == CompanyAddressType.TradingAddress);

        var addresses = await context.CompanyAddresses
            .Where(address => address.CompanyId == result.Value.Id)
            .ToListAsync();

        Assert.Equal(2, addresses.Count);
        Assert.Equal("10 High Street", addresses.Single(address => address.Type == CompanyAddressType.RegisteredOffice).Line1);
        Assert.Equal("10 High Street", addresses.Single(address => address.Type == CompanyAddressType.TradingAddress).Line1);

        var settings = await context.CompanySettings.SingleAsync();
        Assert.Equal(result.Value.Id, settings.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PostalCode_Does_Not_Match_Default_Uk_Regex()
    {
        await using var context = BuildContext();
        var handler = new CreateCompanyHandler(context, new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new CreateCompanyRequest
            {
                Name = "Acme Corporation",
                Addresses =
                [
                    new CreateCompanyAddressRequest
                    {
                        Type = CompanyAddressType.RegisteredOffice,
                        Line1 = "10 High Street",
                        City = "London",
                        PostalCode = "not a postcode",
                        CountryCode = "GB"
                    }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(await context.Companies.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_PostalCode_Is_Omitted()
    {
        await using var context = BuildContext();
        var handler = new CreateCompanyHandler(context, new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new CreateCompanyRequest
            {
                Name = "Acme Corporation",
                Addresses =
                [
                    new CreateCompanyAddressRequest
                    {
                        Type = CompanyAddressType.RegisteredOffice,
                        Line1 = "10 High Street",
                        City = "London",
                        CountryCode = "GB"
                    }
                ]
            },
            CancellationToken.None);

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
