using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.DeactivateEmploymentType;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class DeactivateEmploymentTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Deactivates_EmploymentType()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = EmploymentType.Create(Guid.NewGuid(), companyId, "Casual", null, now);
        context.EmploymentTypes.Add(entity);
        await context.SaveChangesAsync();

        var handler = new DeactivateEmploymentTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateEmploymentTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.EmploymentTypes.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new DeactivateEmploymentTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateEmploymentTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = EmploymentType.Create(Guid.NewGuid(), companyId, "Casual", null, now);
        entity.Deactivate(now);
        context.EmploymentTypes.Add(entity);
        await context.SaveChangesAsync();

        var handler = new DeactivateEmploymentTypeHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateEmploymentTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
