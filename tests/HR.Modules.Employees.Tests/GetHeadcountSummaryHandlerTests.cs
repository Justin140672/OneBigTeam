using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetHeadcountSummary;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetHeadcountSummaryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Employees()
    {
        await using var context = BuildContext();
        var handler = new GetHeadcountSummaryHandler(context);

        var result = await handler.HandleAsync(
            new GetHeadcountSummaryRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Groups_By_Department_And_Counts_Active_And_OnLeave_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var engineering = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var sales = Department.Create(Guid.NewGuid(), companyId, "Sales", null, Now);
        context.Departments.AddRange(engineering, sales);

        var engActive1 = NewEmployee(companyId, "Alice", "Smith");
        engActive1.Assign(engineering.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);
        engActive1.Activate(Now);

        var engActive2 = NewEmployee(companyId, "Bob", "Jones");
        engActive2.Assign(engineering.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);
        engActive2.Activate(Now);

        var engOnLeave = NewEmployee(companyId, "Carol", "White");
        engOnLeave.Assign(engineering.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);
        engOnLeave.Activate(Now);
        engOnLeave.SetOnLeave(Now);

        var salesActive = NewEmployee(companyId, "Dave", "Brown");
        salesActive.Assign(sales.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);
        salesActive.Activate(Now);

        context.Employees.AddRange(engActive1, engActive2, engOnLeave, salesActive);
        await context.SaveChangesAsync();

        var handler = new GetHeadcountSummaryHandler(context);
        var result = await handler.HandleAsync(new GetHeadcountSummaryRequest(companyId), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);

        var engineeringItem = Assert.Single(result.Items, i => i.DepartmentId == engineering.Id);
        Assert.Equal("Engineering", engineeringItem.DepartmentName);
        Assert.Equal(3, engineeringItem.EmployeeCount);

        var salesItem = Assert.Single(result.Items, i => i.DepartmentId == sales.Id);
        Assert.Equal("Sales", salesItem.DepartmentName);
        Assert.Equal(1, salesItem.EmployeeCount);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Draft_Suspended_And_Terminated_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        context.Departments.Add(department);

        var draft = NewEmployee(companyId, "Draft", "Employee");
        draft.Assign(department.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);

        var suspended = NewEmployee(companyId, "Suspended", "Employee");
        suspended.Assign(department.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);
        suspended.Activate(Now);
        suspended.Suspend(Now);

        var terminated = NewEmployee(companyId, "Terminated", "Employee");
        terminated.Assign(department.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);
        terminated.Activate(Now);
        terminated.SetStatusForTesting(EmploymentStatus.FormerEmployee, Now);

        var active = NewEmployee(companyId, "Active", "Employee");
        active.Assign(department.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);
        active.Activate(Now);

        context.Employees.AddRange(draft, suspended, terminated, active);
        await context.SaveChangesAsync();

        var handler = new GetHeadcountSummaryHandler(context);
        var result = await handler.HandleAsync(new GetHeadcountSummaryRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    [Fact]
    public async Task HandleAsync_Buckets_Unresolvable_Department_As_Unassigned()
    {
        // Department is a mandatory Employee field, so a null department is no longer possible.
        // This exercises the remaining "Unassigned" fallback: a department_id that does not
        // resolve to any existing department row (e.g. NewEmployee's randomly generated,
        // never-seeded department id).
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var unassigned = NewEmployee(companyId, "Alice", "Smith");
        unassigned.Activate(Now);
        context.Employees.Add(unassigned);
        await context.SaveChangesAsync();

        var handler = new GetHeadcountSummaryHandler(context);
        var result = await handler.HandleAsync(new GetHeadcountSummaryRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(unassigned.DepartmentId, item.DepartmentId);
        Assert.Equal("Unassigned", item.DepartmentName);
        Assert.Equal(1, item.EmployeeCount);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var employee = NewEmployee(companyId, "Alice", "Smith");
        employee.Activate(Now);

        var otherEmployee = NewEmployee(otherCompanyId, "Bob", "Jones");
        otherEmployee.Activate(Now);

        context.Employees.AddRange(employee, otherEmployee);
        await context.SaveChangesAsync();

        var handler = new GetHeadcountSummaryHandler(context);
        var result = await handler.HandleAsync(new GetHeadcountSummaryRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    private static Employee NewEmployee(Guid companyId, string firstName, string lastName) =>
        Employee.Create(Guid.NewGuid(), companyId, firstName, lastName, $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(), StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
