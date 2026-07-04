using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetDepartment;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetDepartmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Department_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", "Builds the product", Now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var handler = new GetDepartmentHandler(context);
        var result = await handler.HandleAsync(
            new GetDepartmentRequest { CompanyId = companyId, Id = department.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Engineering", result.Value!.Name);
        Assert.Equal("Builds the product", result.Value.Description);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetDepartmentHandler(context);

        var result = await handler.HandleAsync(
            new GetDepartmentRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var department = Department.Create(Guid.NewGuid(), Guid.NewGuid(), "Engineering", null, Now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var handler = new GetDepartmentHandler(context);
        var result = await handler.HandleAsync(
            new GetDepartmentRequest { CompanyId = Guid.NewGuid(), Id = department.Id }, CancellationToken.None);

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
