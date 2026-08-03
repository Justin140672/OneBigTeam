using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmploymentTypeSplit;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetEmploymentTypeSplitHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Employees()
    {
        await using var context = BuildContext();
        var handler = new GetEmploymentTypeSplitHandler(context);

        var result = await handler.HandleAsync(
            new GetEmploymentTypeSplitRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Groups_By_EmploymentType_And_Counts_Active_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var fullTime = EmploymentType.Create(Guid.NewGuid(), companyId, "Full-Time", null, Now);
        var partTime = EmploymentType.Create(Guid.NewGuid(), companyId, "Part-Time", null, Now);
        context.EmploymentTypes.AddRange(fullTime, partTime);

        var fullTime1 = NewEmployee(companyId, "Alice", "Smith", fullTime.Id);
        fullTime1.Activate(Now);
        var fullTime2 = NewEmployee(companyId, "Bob", "Jones", fullTime.Id);
        fullTime2.Activate(Now);
        var fullTime3 = NewEmployee(companyId, "Carol", "White", fullTime.Id);
        fullTime3.Activate(Now);
        var partTimeEmployee = NewEmployee(companyId, "Dave", "Brown", partTime.Id);
        partTimeEmployee.Activate(Now);

        context.Employees.AddRange(fullTime1, fullTime2, fullTime3, partTimeEmployee);
        await context.SaveChangesAsync();

        var handler = new GetEmploymentTypeSplitHandler(context);
        var result = await handler.HandleAsync(new GetEmploymentTypeSplitRequest(companyId), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);

        var fullTimeItem = Assert.Single(result.Items, i => i.EmploymentTypeId == fullTime.Id);
        Assert.Equal("Full-Time", fullTimeItem.EmploymentTypeName);
        Assert.Equal(3, fullTimeItem.EmployeeCount);

        var partTimeItem = Assert.Single(result.Items, i => i.EmploymentTypeId == partTime.Id);
        Assert.Equal("Part-Time", partTimeItem.EmploymentTypeName);
        Assert.Equal(1, partTimeItem.EmployeeCount);
    }

    [Fact]
    public async Task HandleAsync_Buckets_Unresolvable_EmploymentType_As_Not_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        // EmploymentTypeId is a mandatory Employee field, but a fresh randomly generated
        // employment type id that was never seeded as a real EmploymentType row still exercises
        // the handler's "Not Specified" fallback (no matching EmploymentTypes row).
        var unresolved = NewEmployee(companyId, "Alice", "Smith", Guid.NewGuid());
        unresolved.Activate(Now);
        context.Employees.Add(unresolved);
        await context.SaveChangesAsync();

        var handler = new GetEmploymentTypeSplitHandler(context);
        var result = await handler.HandleAsync(new GetEmploymentTypeSplitRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(unresolved.EmploymentTypeId, item.EmploymentTypeId);
        Assert.Equal("Not Specified", item.EmploymentTypeName);
        Assert.Equal(1, item.EmployeeCount);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Draft_Suspended_And_Terminated_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employmentType = EmploymentType.Create(Guid.NewGuid(), companyId, "Full-Time", null, Now);
        context.EmploymentTypes.Add(employmentType);

        var draft = NewEmployee(companyId, "Draft", "Employee", employmentType.Id);

        var suspended = NewEmployee(companyId, "Suspended", "Employee", employmentType.Id);
        suspended.Activate(Now);
        suspended.Suspend(Now);

        var terminated = NewEmployee(companyId, "Terminated", "Employee", employmentType.Id);
        terminated.Activate(Now);
        terminated.SetStatusForTesting(EmploymentStatus.FormerEmployee, Now);

        var active = NewEmployee(companyId, "Active", "Employee", employmentType.Id);
        active.Activate(Now);

        context.Employees.AddRange(draft, suspended, terminated, active);
        await context.SaveChangesAsync();

        var handler = new GetEmploymentTypeSplitHandler(context);
        var result = await handler.HandleAsync(new GetEmploymentTypeSplitRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    [Fact]
    public async Task HandleAsync_Percentages_Sum_To_Exactly_100()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var typeA = EmploymentType.Create(Guid.NewGuid(), companyId, "Type A", null, Now);
        var typeB = EmploymentType.Create(Guid.NewGuid(), companyId, "Type B", null, Now);
        var typeC = EmploymentType.Create(Guid.NewGuid(), companyId, "Type C", null, Now);
        context.EmploymentTypes.AddRange(typeA, typeB, typeC);

        var a = NewEmployee(companyId, "Alice", "Smith", typeA.Id);
        a.Activate(Now);
        var b = NewEmployee(companyId, "Bob", "Jones", typeB.Id);
        b.Activate(Now);
        var c = NewEmployee(companyId, "Carol", "White", typeC.Id);
        c.Activate(Now);

        context.Employees.AddRange(a, b, c);
        await context.SaveChangesAsync();

        var handler = new GetEmploymentTypeSplitHandler(context);
        var result = await handler.HandleAsync(new GetEmploymentTypeSplitRequest(companyId), CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(100.0, result.Items.Sum(i => i.Percentage), precision: 6);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var employee = NewEmployee(companyId, "Alice", "Smith", Guid.NewGuid());
        employee.Activate(Now);

        var otherEmployee = NewEmployee(otherCompanyId, "Bob", "Jones", Guid.NewGuid());
        otherEmployee.Activate(Now);

        context.Employees.AddRange(employee, otherEmployee);
        await context.SaveChangesAsync();

        var handler = new GetEmploymentTypeSplitHandler(context);
        var result = await handler.HandleAsync(new GetEmploymentTypeSplitRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    private static Employee NewEmployee(Guid companyId, string firstName, string lastName, Guid employmentTypeId) =>
        Employee.Create(Guid.NewGuid(), companyId, firstName, lastName, $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(), StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", employmentTypeId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
