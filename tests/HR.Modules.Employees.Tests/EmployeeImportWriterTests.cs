using HR.Infrastructure.Abstractions;
using HR.Modules.Employees;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeeImportWriterTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static EmployeeImportWriter BuildWriter(
        EmployeesDbContext context,
        FakeAuditPublisher? auditPublisher = null,
        FakeProbationDateResolver? probationDateResolver = null) =>
        new(
            context,
            new FakeClock(FixedUtcNow),
            probationDateResolver ?? new FakeProbationDateResolver(),
            auditPublisher ?? new FakeAuditPublisher());

    private static EmployeeImportCreateRequest BuildCreateRequest(
        Guid companyId,
        string workEmail = "alice@example.com",
        Guid? departmentId = null,
        Guid? locationId = null,
        Guid? employmentTypeId = null,
        Guid? positionProfileId = null,
        string? employeeNumber = null,
        Guid? importSessionId = null,
        Guid? actorUserId = null) =>
        new(
            Guid.NewGuid(),
            companyId,
            "Alice",
            "Smith",
            PreferredName: null,
            workEmail,
            PersonalEmail: null,
            StartDate,
            DateOfBirth: null,
            Nationality: null,
            Gender: null,
            departmentId,
            locationId,
            employmentTypeId,
            positionProfileId,
            employeeNumber,
            importSessionId ?? Guid.NewGuid(),
            actorUserId ?? Guid.NewGuid());

    [Fact]
    public async Task CreateEmployeeAsync_Creates_Active_Employee_And_Returns_Expected_Result()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var writer = BuildWriter(context);

        var request = BuildCreateRequest(companyId);

        var result = await writer.CreateEmployeeAsync(request, CancellationToken.None);

        Assert.Equal(request.Id, result.EmployeeId);
        Assert.Equal(StartDate, result.StartDate);
        Assert.Equal(StartDate.AddMonths(6), result.ProbationEndDate);

        var saved = await context.Employees.SingleAsync(e => e.Id == request.Id);
        Assert.Equal(EmploymentStatus.Active, saved.Status);
        Assert.Equal("alice@example.com", saved.WorkEmail);
        Assert.Equal("Alice", saved.PreferredName);
    }

    [Fact]
    public async Task CreateEmployeeAsync_Publishes_EmployeeCreatedAuditEvent_With_Import_Source()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();
        var writer = BuildWriter(context, auditPublisher);
        var actorUserId = Guid.NewGuid();
        var importSessionId = Guid.NewGuid();

        var request = BuildCreateRequest(companyId, actorUserId: actorUserId, importSessionId: importSessionId);

        var result = await writer.CreateEmployeeAsync(request, CancellationToken.None);

        var published = Assert.Single(auditPublisher.Published);
        var evt = Assert.IsType<EmployeeCreatedAuditEvent>(published);
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(result.EmployeeId, evt.EmployeeId);
        Assert.Equal(actorUserId, evt.ActorUserId);
        Assert.Equal("Import", evt.Source);
        Assert.Equal(importSessionId, evt.ImportSessionId);
    }

    [Fact]
    public async Task CreateEmployeeAsync_Assigns_Department_Location_And_PositionProfile_When_Provided()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "HQ", null, now);
        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Developer", null, null, null, null, null, null, null, null, now);
        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);
        var request = BuildCreateRequest(
            companyId, departmentId: department.Id, locationId: location.Id, positionProfileId: profile.Id);

        await writer.CreateEmployeeAsync(request, CancellationToken.None);

        var saved = await context.Employees.SingleAsync(e => e.Id == request.Id);
        Assert.Equal(department.Id, saved.DepartmentId);
        Assert.Equal(location.Id, saved.LocationId);
        Assert.Equal(profile.Id, saved.PositionProfileId);
    }

    [Fact]
    public async Task CreateEmployeeAsync_Uses_PositionProfile_ProbationOverride_When_Present()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Senior Dev", null, 3, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context, probationDateResolver: new FakeProbationDateResolver(months: 6));
        var request = BuildCreateRequest(companyId, positionProfileId: profile.Id);

        var result = await writer.CreateEmployeeAsync(request, CancellationToken.None);

        Assert.Equal(StartDate.AddMonths(3), result.ProbationEndDate);
    }

    [Fact]
    public async Task TryAssignManagerAsync_Assigns_Manager_When_No_Cycle()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, now);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var assigned = await writer.TryAssignManagerAsync(companyId, employee.Id, manager.Id, CancellationToken.None);

        Assert.True(assigned);
        var saved = await context.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.Equal(manager.Id, saved.ManagerId);
    }

    [Fact]
    public async Task TryAssignManagerAsync_Returns_False_For_Direct_Circular_Assignment()
    {
        // A -> B (B reports to A), then try to assign B as manager of A.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var empA = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        var empB = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, now);
        empB.Assign(null, null, null, empA.Id, now);
        context.Employees.AddRange(empA, empB);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var assigned = await writer.TryAssignManagerAsync(companyId, empA.Id, empB.Id, CancellationToken.None);

        Assert.False(assigned);
        var saved = await context.Employees.SingleAsync(e => e.Id == empA.Id);
        Assert.Null(saved.ManagerId);
    }

    [Fact]
    public async Task TryAssignManagerAsync_Returns_False_For_Deep_Circular_Assignment()
    {
        // A -> B -> C, then try to assign C as manager of A.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var empA = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        var empB = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, now);
        var empC = Employee.Create(Guid.NewGuid(), companyId, "Carol", "White", "carol@example.com", StartDate, hasSystemAccess: true, now);
        empB.Assign(null, null, null, empA.Id, now);
        empC.Assign(null, null, null, empB.Id, now);
        context.Employees.AddRange(empA, empB, empC);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var assigned = await writer.TryAssignManagerAsync(companyId, empA.Id, empC.Id, CancellationToken.None);

        Assert.False(assigned);
    }

    [Fact]
    public async Task TryAssignManagerAsync_Returns_False_When_Manager_Is_Terminated()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, now);
        manager.Terminate(now);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.AddRange(manager, employee);
        await context.SaveChangesAsync();

        var writer = BuildWriter(context);

        var assigned = await writer.TryAssignManagerAsync(companyId, employee.Id, manager.Id, CancellationToken.None);

        Assert.False(assigned);
    }

    [Fact]
    public async Task TryAssignManagerAsync_Returns_False_When_Employee_Or_Manager_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var writer = BuildWriter(context);

        var assigned = await writer.TryAssignManagerAsync(companyId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(assigned);
    }
}
