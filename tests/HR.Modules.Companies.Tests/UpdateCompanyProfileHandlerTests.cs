using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompanyProfile;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanyProfileHandlerTests
{
    [Fact]
    public async Task HandleAsync_Updates_Company_Name_And_UpdatedAt()
    {
        await using var context = BuildContext();
        var createdAt = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var updatedAt = new DateTime(2026, 6, 5, 11, 30, 0, DateTimeKind.Utc);
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", "acme-corporation", createdAt);

        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanyProfileHandler(context, new FakeClock(updatedAt));

        var result = await handler.HandleAsync(
            new UpdateCompanyProfileRequest
            {
                Id = company.Id,
                Name = "Acme Global"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(company.Id, result.Value!.Id);
        Assert.Equal("Acme Global", result.Value.Name);
        Assert.Equal("acme-corporation", result.Value.Slug);
        Assert.Equal(new DateTimeOffset(updatedAt), result.Value.UpdatedAt);

        var updatedCompany = await context.Companies.SingleAsync(c => c.Id == company.Id);
        Assert.Equal("Acme Global", updatedCompany.Name);
        Assert.Equal(new DateTimeOffset(updatedAt), updatedCompany.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateCompanyProfileHandler(context, new FakeClock(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc)));
        var unknownCompanyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new UpdateCompanyProfileRequest
            {
                Id = unknownCompanyId,
                Name = "Acme Global"
            },
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

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }
}