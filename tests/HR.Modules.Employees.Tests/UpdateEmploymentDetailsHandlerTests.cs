using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmploymentDetails;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateEmploymentDetailsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    // ── probation date — employee override ────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Sets_ProbationEndDate_When_Explicitly_Provided()
    {
        // HR can override the calculated probation end date for a specific employee
        // by supplying an explicit date via UpdateEmploymentDetails.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        employee.SetProbationEndDate(StartDate.AddMonths(6), now); // set from company default at creation
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));
        var manualOverrideDate = new DateOnly(2027, 3, 15);

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            ProbationEndDate = manualOverrideDate
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(manualOverrideDate, result.Value!.ProbationEndDate);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(manualOverrideDate, saved.ProbationEndDate);
    }

    [Fact]
    public async Task HandleAsync_Clears_ProbationEndDate_When_Null_Supplied()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        employee.SetProbationEndDate(StartDate.AddMonths(6), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            ProbationEndDate = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ProbationEndDate);
        var saved = await context.Employees.SingleAsync();
        Assert.Null(saved.ProbationEndDate);
    }

    // ── baseline handler behaviour ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Status = EmploymentStatus.Active,
            StartDate = StartDate
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Updates_Status_To_Active()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmploymentStatus.Active, result.Value!.Status);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Active, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        var otherCompanyDept = Department.Create(Guid.NewGuid(), Guid.NewGuid(), "Eng", null, now);
        context.Employees.Add(employee);
        context.Departments.Add(otherCompanyDept);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            DepartmentId = otherCompanyDept.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Persists_Valid_LocationId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, now);
        context.Employees.Add(employee);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            LocationId = location.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(location.Id, result.Value!.LocationId);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(location.Id, saved.LocationId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Location_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        var otherCompanyId = Guid.NewGuid();
        var otherCompanyLocationType = LocationType.Create(Guid.NewGuid(), otherCompanyId, "Office", null, now);
        var otherCompanyLocation = Location.Create(Guid.NewGuid(), otherCompanyId, otherCompanyLocationType.Id, "Head Office", null, now);
        context.Employees.Add(employee);
        context.LocationTypes.Add(otherCompanyLocationType);
        context.Locations.Add(otherCompanyLocation);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            LocationId = otherCompanyLocation.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    // ── Draft status transition ──────────────────────────────────────────────
    // Draft isn't a selectable option on the Employment tab's status dropdown — it's only ever a
    // brand-new employee's starting state before their first Activate. A Draft employee's edit
    // that doesn't touch status at all still round-trips Status == Draft unchanged (see
    // EmployeeEmploymentTab.razor's PopulateModel), which must be allowed; only an actual attempt
    // to revert an already-progressed employee back to Draft should be rejected.

    [Fact]
    public async Task HandleAsync_Allows_Draft_Employee_Edit_That_Leaves_Status_Unchanged()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now); // starts Draft, never Activated
        var manager = CreateEmployee(companyId, now);
        context.Employees.AddRange(employee, manager);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Draft,
            StartDate = StartDate,
            ManagerId = manager.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.Equal(EmploymentStatus.Draft, saved.Status);
        Assert.Equal(manager.Id, saved.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Reverting_Active_Employee_To_Draft()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        employee.Activate(now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmploymentDetailsHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Draft,
            StartDate = StartDate
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Active, saved.Status);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Employee CreateEmployee(Guid companyId, DateTimeOffset now)
        => Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options);
    }
}
