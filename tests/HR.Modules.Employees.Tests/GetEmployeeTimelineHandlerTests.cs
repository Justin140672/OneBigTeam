using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmployeeTimeline;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetEmployeeTimelineHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employee CreateEmployee(Guid companyId, Guid? managerId = null)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", $"alice{Guid.NewGuid():N}@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        if (managerId.HasValue)
            employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, managerId.Value, Now);

        return employee;
    }

    private static EmployeeTimelineEntry CreateEntry(
        Guid companyId,
        Guid employeeId,
        EmployeeTimelineVisibility visibility,
        DateOnly eventDate,
        DateTimeOffset createdDate,
        EmployeeTimelineCategory category = EmployeeTimelineCategory.Employment,
        EmployeeTimelineEventType eventType = EmployeeTimelineEventType.EmployeePromoted,
        string title = "Some event",
        string summary = "Some event happened.") =>
        EmployeeTimelineEntry.Create(
            Guid.NewGuid(), companyId, employeeId, eventDate, eventType, category, title, summary,
            performedByUserId: null, "Employees", sourceRecordId: Guid.NewGuid(), visibility, createdDate);

    private static GetEmployeeTimelineHandler BuildHandler(EmployeesDbContext context) =>
        new(context, new FakeEmployeeNameReader());

    private static GetEmployeeTimelineRequest BuildRequest(
        Guid companyId,
        Guid employeeId,
        EmployeeTimelineCategory? category = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        int pageNumber = 1,
        int pageSize = 20) =>
        new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Category = category,
            DateFrom = dateFrom,
            DateTo = dateTo,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), callerIsHr: true, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var employee = CreateEmployee(Guid.NewGuid());
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), employee.Id), Guid.NewGuid(), callerIsHr: true, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Hr_Sees_Entries_Across_All_Visibility_Tiers()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId);
        context.Employees.Add(employee);

        context.EmployeeTimelineEntries.AddRange(
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.HrOnly, new DateOnly(2026, 7, 1), Now),
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.EmployeeAndHr, new DateOnly(2026, 7, 2), Now),
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 7, 3), Now));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id), Guid.NewGuid(), callerIsHr: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Self_Sees_Only_EmployeeAndHr_And_AuthorisedInternal_Tier_Entries()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId);
        context.Employees.Add(employee);

        context.EmployeeTimelineEntries.AddRange(
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.HrOnly, new DateOnly(2026, 7, 1), Now),
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.EmployeeAndHr, new DateOnly(2026, 7, 2), Now),
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 7, 3), Now));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id), employee.Id, callerIsHr: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, i => Assert.NotEqual(EmployeeTimelineEventType.HrNoteAdded, i.EventType));
    }

    [Fact]
    public async Task HandleAsync_Manager_Sees_Only_AuthorisedInternal_Tier_Entries_For_Direct_Reports()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, managerId);
        context.Employees.Add(employee);

        context.EmployeeTimelineEntries.AddRange(
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.HrOnly, new DateOnly(2026, 7, 1), Now),
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.EmployeeAndHr, new DateOnly(2026, 7, 2), Now),
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 7, 3), Now));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id), managerId, callerIsHr: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Manager_Does_Not_See_Entries_For_NonReports()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, managerId: Guid.NewGuid());
        context.Employees.Add(employee);

        context.EmployeeTimelineEntries.Add(
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 7, 3), Now));
        await context.SaveChangesAsync();

        var unrelatedManagerId = Guid.NewGuid();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id), unrelatedManagerId, callerIsHr: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Unrelated_Caller_Gets_Empty_Successful_Result()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId);
        context.Employees.Add(employee);

        context.EmployeeTimelineEntries.Add(
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 7, 3), Now));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id), Guid.NewGuid(), callerIsHr: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Category()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId);
        context.Employees.Add(employee);

        context.EmployeeTimelineEntries.AddRange(
            CreateEntry(
                companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 7, 1), Now,
                category: EmployeeTimelineCategory.Employment),
            CreateEntry(
                companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 7, 2), Now,
                category: EmployeeTimelineCategory.Compensation));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, category: EmployeeTimelineCategory.Compensation),
            Guid.NewGuid(), callerIsHr: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(EmployeeTimelineCategory.Compensation, result.Value.Items[0].Category);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Date_Range()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId);
        context.Employees.Add(employee);

        context.EmployeeTimelineEntries.AddRange(
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 6, 1), Now),
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 7, 15), Now),
            CreateEntry(companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, new DateOnly(2026, 8, 1), Now));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, employee.Id,
                dateFrom: new DateOnly(2026, 7, 1), dateTo: new DateOnly(2026, 7, 31)),
            Guid.NewGuid(), callerIsHr: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(new DateOnly(2026, 7, 15), result.Value.Items[0].EventDate);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_EventDate_Then_CreatedDate_Descending()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId);
        context.Employees.Add(employee);

        var sameDate = new DateOnly(2026, 7, 10);
        context.EmployeeTimelineEntries.AddRange(
            CreateEntry(
                companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, sameDate,
                Now, title: "Earlier created"),
            CreateEntry(
                companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, sameDate,
                Now.AddHours(1), title: "Later created"),
            CreateEntry(
                companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal, sameDate.AddDays(-5),
                Now, title: "Older event date"));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id), Guid.NewGuid(), callerIsHr: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;
        Assert.Equal(3, items.Count);
        Assert.Equal("Later created", items[0].Title);
        Assert.Equal("Earlier created", items[1].Title);
        Assert.Equal("Older event date", items[2].Title);
    }

    [Fact]
    public async Task HandleAsync_Paginates_Results()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId);
        context.Employees.Add(employee);

        for (var i = 0; i < 5; i++)
        {
            context.EmployeeTimelineEntries.Add(CreateEntry(
                companyId, employee.Id, EmployeeTimelineVisibility.AuthorisedInternal,
                new DateOnly(2026, 7, 1).AddDays(i), Now));
        }
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var page1 = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, pageNumber: 1, pageSize: 2),
            Guid.NewGuid(), callerIsHr: true, CancellationToken.None);
        var page2 = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, pageNumber: 2, pageSize: 2),
            Guid.NewGuid(), callerIsHr: true, CancellationToken.None);

        Assert.True(page1.IsSuccess);
        Assert.True(page2.IsSuccess);
        Assert.Equal(5, page1.Value!.TotalCount);
        Assert.Equal(2, page1.Value.Items.Count);
        Assert.Equal(2, page2.Value!.Items.Count);
        Assert.Equal(3, page1.Value.TotalPages);
        Assert.NotEqual(page1.Value.Items[0].Id, page2.Value.Items[0].Id);
    }
}
