using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateDepartment;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateDepartmentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedOffset = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Updates_Department_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = companyId,
                Id = dept.Id,
                Name = "Platform Engineering"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Platform Engineering", result.Value!.Name);

        var saved = await context.Departments.SingleAsync();
        Assert.Equal("Platform Engineering", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_Updates_Description_And_Parent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var parent = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        var child = Department.Create(Guid.NewGuid(), companyId, "Platform", null, FixedOffset);
        context.Departments.AddRange(parent, child);
        await context.SaveChangesAsync();

        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = companyId,
                Id = child.Id,
                Name = "Platform",
                Description = "Core platform team",
                ParentDepartmentId = parent.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(parent.Id, result.Value!.ParentDepartmentId);
        Assert.Equal("Core platform team", result.Value.Description);
    }

    [Fact]
    public async Task HandleAsync_Updates_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = companyId,
                Id = dept.Id,
                Name = "Engineering",
                ManagerEmployeeId = managerId
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(managerId, result.Value!.ManagerEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = Guid.NewGuid(),
                Name = "Engineering"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Inactive_Department()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var dept = Department.Create(Guid.NewGuid(), companyId, "Legacy", null, FixedOffset);
        dept.Deactivate(FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = companyId,
                Id = dept.Id,
                Name = "Legacy"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_New_Name_Already_Used()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.Departments.AddRange(
            Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset),
            Department.Create(Guid.NewGuid(), companyId, "People", null, FixedOffset));
        await context.SaveChangesAsync();

        var engineering = await context.Departments.SingleAsync(d => d.Name == "Engineering");

        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = companyId,
                Id = engineering.Id,
                Name = "People"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Keeping_Same_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = companyId,
                Id = dept.Id,
                Name = "Engineering",
                Description = "Updated description"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Engineering", result.Value!.Name);
        Assert.Equal("Updated description", result.Value.Description);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Parent_Is_Self()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = companyId,
                Id = dept.Id,
                Name = "Engineering",
                ParentDepartmentId = dept.Id
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Parent_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var dept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, FixedOffset);
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new UpdateDepartmentHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateDepartmentRequest
            {
                CompanyId = companyId,
                Id = dept.Id,
                Name = "Engineering",
                ParentDepartmentId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
