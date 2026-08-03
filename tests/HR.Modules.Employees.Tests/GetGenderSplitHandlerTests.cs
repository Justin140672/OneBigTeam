using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetGenderSplit;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetGenderSplitHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Employees()
    {
        await using var context = BuildContext();
        var handler = new GetGenderSplitHandler(context);

        var result = await handler.HandleAsync(
            new GetGenderSplitRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Groups_By_Gender_And_Counts_Active_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var female1 = NewEmployee(companyId, "Alice", "Smith", "Female");
        female1.Activate(Now);
        var female2 = NewEmployee(companyId, "Carol", "White", "Female");
        female2.Activate(Now);
        var male1 = NewEmployee(companyId, "Bob", "Jones", "Male");
        male1.Activate(Now);

        context.Employees.AddRange(female1, female2, male1);
        await context.SaveChangesAsync();

        var handler = new GetGenderSplitHandler(context);
        var result = await handler.HandleAsync(new GetGenderSplitRequest(companyId), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);

        var female = Assert.Single(result.Items, i => i.Gender == "Female");
        Assert.Equal(2, female.EmployeeCount);

        var male = Assert.Single(result.Items, i => i.Gender == "Male");
        Assert.Equal(1, male.EmployeeCount);
    }

    [Fact]
    public async Task HandleAsync_Buckets_Null_Or_Whitespace_Gender_As_Not_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        // Gender is a required (non-nullable) EF property, so null/empty values can never be
        // persisted — only whitespace-only values can reach the database while still being
        // "not really specified" from a reporting perspective. Cover that case three times.
        var whitespaceGender1 = NewEmployee(companyId, "Alice", "Smith", "   ");
        whitespaceGender1.Activate(Now);
        var whitespaceGender2 = NewEmployee(companyId, "Bob", "Jones", " ");
        whitespaceGender2.Activate(Now);
        var whitespaceGender3 = NewEmployee(companyId, "Carol", "White", "\t");
        whitespaceGender3.Activate(Now);

        context.Employees.AddRange(whitespaceGender1, whitespaceGender2, whitespaceGender3);
        await context.SaveChangesAsync();

        var handler = new GetGenderSplitHandler(context);
        var result = await handler.HandleAsync(new GetGenderSplitRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Not Specified", item.Gender);
        Assert.Equal(3, item.EmployeeCount);
        Assert.Equal(100.0, item.Percentage, precision: 6);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Draft_Suspended_And_Terminated_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var draft = NewEmployee(companyId, "Draft", "Employee", "Female");

        var suspended = NewEmployee(companyId, "Suspended", "Employee", "Female");
        suspended.Activate(Now);
        suspended.Suspend(Now);

        var terminated = NewEmployee(companyId, "Terminated", "Employee", "Female");
        terminated.Activate(Now);
        terminated.SetStatusForTesting(EmploymentStatus.FormerEmployee, Now);

        var active = NewEmployee(companyId, "Active", "Employee", "Female");
        active.Activate(Now);

        context.Employees.AddRange(draft, suspended, terminated, active);
        await context.SaveChangesAsync();

        var handler = new GetGenderSplitHandler(context);
        var result = await handler.HandleAsync(new GetGenderSplitRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    [Fact]
    public async Task HandleAsync_Percentages_Sum_To_Exactly_100()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        // 1 of 3 -> 33.3, 1 of 3 -> 33.3, 1 of 3 -> 33.3 which sums to 99.9 without adjustment.
        var female = NewEmployee(companyId, "Alice", "Smith", "Female");
        female.Activate(Now);
        var male = NewEmployee(companyId, "Bob", "Jones", "Male");
        male.Activate(Now);
        var other = NewEmployee(companyId, "Carol", "White", "Non-binary");
        other.Activate(Now);

        context.Employees.AddRange(female, male, other);
        await context.SaveChangesAsync();

        var handler = new GetGenderSplitHandler(context);
        var result = await handler.HandleAsync(new GetGenderSplitRequest(companyId), CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(100.0, result.Items.Sum(i => i.Percentage), precision: 6);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var employee = NewEmployee(companyId, "Alice", "Smith", "Female");
        employee.Activate(Now);

        var otherEmployee = NewEmployee(otherCompanyId, "Bob", "Jones", "Male");
        otherEmployee.Activate(Now);

        context.Employees.AddRange(employee, otherEmployee);
        await context.SaveChangesAsync();

        var handler = new GetGenderSplitHandler(context);
        var result = await handler.HandleAsync(new GetGenderSplitRequest(companyId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.EmployeeCount);
    }

    private static Employee NewEmployee(Guid companyId, string firstName, string lastName, string gender) =>
        Employee.Create(Guid.NewGuid(), companyId, firstName, lastName, $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(), StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", gender, "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
