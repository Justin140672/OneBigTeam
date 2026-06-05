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
        var company = Company.Create(Guid.NewGuid(), "Acme", "acme", now);
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
                    CountryCode = "GB",
                }
            ]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme Corporation", result.Value!.Name);
        Assert.Equal(2, result.Value.Addresses.Count);
        Assert.Null(result.Value.Branding);

        var saved = await context.Companies
            .Include(c => c.Addresses)
            .SingleAsync(c => c.Id == company.Id);
        Assert.Equal("Acme Corporation", saved.Name);
        Assert.Equal(2, saved.Addresses.Count);
    }

    [Fact]
    public async Task HandleAsync_Creates_Branding_When_Not_Previously_Set()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", "acme", now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanyHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(new UpdateCompanyRequest
        {
            Id = company.Id,
            Name = "Acme",
            Addresses =
            [
                new UpdateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "1 Main St",
                    City = "London",
                    CountryCode = "GB",
                }
            ],
            Branding = new UpdateCompanyBrandingRequest
            {
                PrimaryColor = "#FF5733",
                SecondaryColor = "#C70039",
                AccentColor = "#900C3F",
            }
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Branding);
        Assert.Equal("#FF5733", result.Value.Branding!.PrimaryColor);
        Assert.Equal("#C70039", result.Value.Branding.SecondaryColor);
        Assert.Equal("#900C3F", result.Value.Branding.AccentColor);

        var branding = await context.CompanyBranding.SingleAsync();
        Assert.Equal(company.Id, branding.CompanyId);
        Assert.Equal("#FF5733", branding.PrimaryColor);
    }

    [Fact]
    public async Task HandleAsync_Updates_Existing_Branding_Colors()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", "acme", now);
        var branding = CompanyBranding.CreateDefault(company.Id, now);
        context.Companies.Add(company);
        context.CompanyBranding.Add(branding);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanyHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(new UpdateCompanyRequest
        {
            Id = company.Id,
            Name = "Acme",
            Addresses =
            [
                new UpdateCompanyAddressRequest
                {
                    Type = CompanyAddressType.RegisteredOffice,
                    Line1 = "1 Main St",
                    City = "London",
                    CountryCode = "GB",
                }
            ],
            Branding = new UpdateCompanyBrandingRequest
            {
                PrimaryColor = "#123456",
                SecondaryColor = "#ABCDEF",
                AccentColor = "#FEDCBA",
            }
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("#123456", result.Value!.Branding!.PrimaryColor);

        var savedBranding = await context.CompanyBranding.SingleAsync();
        Assert.Equal("#123456", savedBranding.PrimaryColor);
        Assert.Equal("#ABCDEF", savedBranding.SecondaryColor);
        Assert.Equal("#FEDCBA", savedBranding.AccentColor);
    }

    [Fact]
    public async Task HandleAsync_Skips_Branding_When_Not_Provided()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", "acme", now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanyHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        var result = await handler.HandleAsync(new UpdateCompanyRequest
        {
            Id = company.Id,
            Name = "Acme",
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

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Branding);
        Assert.Empty(context.CompanyBranding);
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
            Id = Guid.NewGuid(),
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

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
