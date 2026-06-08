using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateDepartment;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CreateDepartmentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Department()
    {
        await using var context = BuildContext();
        var handler = new CreateDepartmentHandler(context, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateDepartmentRequest { CompanyId = companyId, Name = "Engineering" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Engineering", result.Value.Name);
        Assert.Null(result.Value.Description);
        Assert.Null(result.Value.ParentDepartmentId);
        Assert.True(result.Value.IsActive);

        var saved = await context.Departments.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Creates_Department_With_Description_And_Parent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var parent = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        context.Departments.Add(parent);
        await context.SaveChangesAsync();

        var handler = new CreateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateDepartmentRequest
            {
                CompanyId = companyId,
                Name = "Platform",
                Description = "Platform engineering sub-team",
                ParentDepartmentId = parent.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(parent.Id, result.Value!.ParentDepartmentId);
        Assert.Equal("Platform engineering sub-team", result.Value.Description);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Departments.Add(Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now));
        await context.SaveChangesAsync();

        var handler = new CreateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateDepartmentRequest { CompanyId = companyId, Name = "Engineering" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_Found_When_Parent_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateDepartmentRequest
            {
                CompanyId = Guid.NewGuid(),
                Name = "Platform",
                ParentDepartmentId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_Found_When_Parent_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var otherCompanyId = Guid.NewGuid();
        var parent = Department.Create(Guid.NewGuid(), otherCompanyId, "Engineering", null, now);
        context.Departments.Add(parent);
        await context.SaveChangesAsync();

        var handler = new CreateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateDepartmentRequest
            {
                CompanyId = Guid.NewGuid(),
                Name = "Platform",
                ParentDepartmentId = parent.Id
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_In_Different_Companies()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        context.Departments.Add(Department.Create(Guid.NewGuid(), companyA, "Engineering", null, now));
        await context.SaveChangesAsync();

        var handler = new CreateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateDepartmentRequest { CompanyId = companyB, Name = "Engineering" },
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
