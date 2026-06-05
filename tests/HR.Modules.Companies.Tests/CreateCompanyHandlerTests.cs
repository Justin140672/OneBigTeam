using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.CreateCompany;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
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

        var company = await context.Companies.SingleAsync();
        Assert.Equal(result.Value.Id, company.Id);
        Assert.Equal("Acme Corporation", company.Name);
        Assert.Equal("acme-corporation", company.Slug);
        Assert.True(company.IsActive);
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

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }
}
