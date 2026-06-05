using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetCompany;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class GetCompanyHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_Company_When_It_Exists()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", "acme-corporation", now);
        var address = CompanyAddress.Create(
            Guid.NewGuid(),
            company.Id,
            CompanyAddressType.RegisteredOffice,
            "10 High Street",
            null,
            "London",
            null,
            "SW1A 1AA",
            "GB",
            now);
        company.SetAddress(address, now);

        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetCompanyHandler(context);

        var result = await handler.HandleAsync(
            new GetCompanyRequest { Id = company.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(company.Id, result.Value!.Id);
        Assert.Equal("Acme Corporation", result.Value.Name);
        Assert.Equal("acme-corporation", result.Value.Slug);
        Assert.True(result.Value.IsActive);
        Assert.Equal(now, result.Value.CreatedAt);
        Assert.Single(result.Value.Addresses);
        Assert.Equal(CompanyAddressType.RegisteredOffice, result.Value.Addresses.Single().Type);
        Assert.Equal("London", result.Value.Addresses.Single().City);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetCompanyHandler(context);
        var unknownCompanyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new GetCompanyRequest { Id = unknownCompanyId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Contains(unknownCompanyId.ToString(), result.Error.Message);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}