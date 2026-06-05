using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.CreateCompany;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CreateCompanyHandlerTests
{
    [Fact]
    public async Task HandleAsync_Creates_Company_With_Generated_Slug()
    {
        await using var context = BuildContext();
        var handler = new CreateCompanyHandler(context, new FakeClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(
            new CreateCompanyRequest { Name = "Acme Corporation" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Acme Corporation", result.Value!.Name);
        Assert.Equal("acme-corporation", result.Value.Slug);
        Assert.Equal("#0055AA", result.Value.Branding.PrimaryColor);
        Assert.Equal("#1F2937", result.Value.Branding.SecondaryColor);
        Assert.Equal("#0EA5E9", result.Value.Branding.AccentColor);
        Assert.Empty(result.Value.Addresses);

        var company = await context.Companies.SingleAsync();
        Assert.Equal(result.Value.Id, company.Id);
        Assert.Equal("Acme Corporation", company.Name);
        Assert.Equal("acme-corporation", company.Slug);
        Assert.True(company.IsActive);

        var settings = await context.CompanySettings.SingleAsync();
        Assert.Equal(company.Id, settings.CompanyId);
        Assert.Equal("UTC", settings.TimeZone);
        Assert.Equal("en-GB", settings.Locale);
        Assert.Equal(
            WorkingDays.Monday
            | WorkingDays.Tuesday
            | WorkingDays.Wednesday
            | WorkingDays.Thursday
            | WorkingDays.Friday,
            settings.WorkingWeek);
        Assert.Equal(1, settings.LeaveYearStartMonth);
        Assert.Equal(25, settings.DefaultHolidayAllowance);
        Assert.Equal(6, settings.ProbationMonths);

        var branding = await context.CompanyBranding.SingleAsync();
        Assert.Equal(company.Id, branding.CompanyId);
        Assert.Equal("#0055AA", branding.PrimaryColor);
        Assert.Equal("#1F2937", branding.SecondaryColor);
        Assert.Equal("#0EA5E9", branding.AccentColor);
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
        Assert.Equal("#0055AA", result.Value.Branding.PrimaryColor);
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

        var branding = await context.CompanyBranding.SingleAsync();
        Assert.Equal(result.Value.Id, branding.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Slug_Already_Exists()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));

        context.Companies.Add(Company.Create(Guid.NewGuid(), "Existing Company", "existing-company", now));
        await context.SaveChangesAsync();

        var handler = new CreateCompanyHandler(context, new FakeClock(now.UtcDateTime));

        var result = await handler.HandleAsync(
            new CreateCompanyRequest { Name = "Existing Company" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("existing-company", result.Error.Message);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
