using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmploymentType;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CreateEmploymentTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_EmploymentType()
    {
        await using var context = BuildContext();
        var handler = new CreateEmploymentTypeHandler(context, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateEmploymentTypeRequest { CompanyId = companyId, Name = "Permanent", Description = "Full-time" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("Permanent", result.Value.Name);
        Assert.Equal("Full-time", result.Value.Description);
        Assert.True(result.Value.IsActive);

        var saved = await context.EmploymentTypes.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.EmploymentTypes.Add(EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, now));
        await context.SaveChangesAsync();

        var handler = new CreateEmploymentTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmploymentTypeRequest { CompanyId = companyId, Name = "Permanent" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_In_Different_Companies()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        context.EmploymentTypes.Add(EmploymentType.Create(Guid.NewGuid(), companyA, "Permanent", null, now));
        await context.SaveChangesAsync();

        var handler = new CreateEmploymentTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateEmploymentTypeRequest { CompanyId = companyB, Name = "Permanent" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
