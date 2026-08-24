using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmploymentDetails;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateEmploymentDetailsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static UpdateEmploymentDetailsHandler BuildHandler(
        EmployeesDbContext context,
        FakeClock clock,
        IIntegrationEventPublisher? integrationEventPublisher = null,
        FakeAuditPublisher? auditPublisher = null)
        => new(
            context,
            clock,
            integrationEventPublisher ?? new NoOpIntegrationEventPublisher(),
            auditPublisher ?? new FakeAuditPublisher(),
            new FakeCompanyEmployeeNumberSettingsReader());

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));
        var manualOverrideDate = new DateOnly(2027, 3, 15);

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            ProbationEndDate = manualOverrideDate
        }, Guid.NewGuid(), CancellationToken.None);

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            ProbationEndDate = null
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ProbationEndDate);
        var saved = await context.Employees.SingleAsync();
        Assert.Null(saved.ProbationEndDate);
    }

    // ── notice period override ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Persists_NoticePeriodOverride_When_Both_Fields_Provided()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            NoticePeriodUnitOverride = NoticePeriodUnit.Weeks,
            NoticePeriodLengthOverride = 4
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NoticePeriodUnit.Weeks, result.Value!.NoticePeriodUnitOverride);
        Assert.Equal(4, result.Value.NoticePeriodLengthOverride);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(NoticePeriodUnit.Weeks, saved.NoticePeriodUnitOverride);
        Assert.Equal(4, saved.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task HandleAsync_Clears_NoticePeriodOverride_When_Null_Fields_Supplied()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        employee.UpdateEmploymentDetails(
            employee.EmployeeNumber, employee.EmploymentTypeId, employee.StartDate,
            employee.ContinuousServiceDate, employee.ProbationEndDate, employee.LeavingDate,
            employee.Notes, now, NoticePeriodUnit.Months, 2);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            NoticePeriodUnitOverride = null,
            NoticePeriodLengthOverride = null
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.NoticePeriodUnitOverride);
        Assert.Null(result.Value.NoticePeriodLengthOverride);
        var saved = await context.Employees.SingleAsync();
        Assert.Null(saved.NoticePeriodUnitOverride);
        Assert.Null(saved.NoticePeriodLengthOverride);
    }

    // ── baseline handler behaviour ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Status = EmploymentStatus.Active,
            StartDate = StartDate
        }, Guid.NewGuid(), CancellationToken.None);

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate
        }, Guid.NewGuid(), CancellationToken.None);

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate
        }, Guid.NewGuid(), CancellationToken.None);

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            DepartmentId = otherCompanyDept.Id
        }, Guid.NewGuid(), CancellationToken.None);

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            LocationId = location.Id
        }, Guid.NewGuid(), CancellationToken.None);

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            LocationId = otherCompanyLocation.Id
        }, Guid.NewGuid(), CancellationToken.None);

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Draft,
            StartDate = StartDate,
            ManagerId = manager.Id
        }, Guid.NewGuid(), CancellationToken.None);

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

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Draft,
            StartDate = StartDate
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(EmploymentStatus.Active, saved.Status);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    // ── granular integration events ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Publishes_PositionChanged_LocationChanged_And_ManagerChanged_When_All_Change()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = CreateEmployee(companyId, now);
        context.Employees.Add(manager);

        var previousDeptId = Guid.NewGuid();
        var previousPositionId = Guid.NewGuid();
        var previousLocationId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0002", Guid.NewGuid(), previousDeptId, previousLocationId, previousPositionId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var newPositionId = Guid.NewGuid();
        var newLocationId = Guid.NewGuid();

        // Ensure new position/location/department pass their "exists and active" checks.
        var newDept = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        context.Departments.Add(newDept);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        context.LocationTypes.Add(locationType);
        var newLocation = Location.Create(newLocationId, companyId, locationType.Id, "Remote", null, now);
        context.Locations.Add(newLocation);
        var newPosition = PositionProfile.Create(newPositionId, companyId, newDept.Id, newLocationId, "Senior Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(newPosition);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = employee.Status,
            StartDate = StartDate,
            DepartmentId = newDept.Id,
            PositionProfileId = newPositionId,
            LocationId = newLocationId,
            ManagerId = manager.Id
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, publisher.Published.Count);

        var positionEvt = Assert.Single(publisher.Published.OfType<HR.Modules.Employees.Contracts.EmployeePositionChangedIntegrationEvent>());
        Assert.Equal(previousPositionId, positionEvt.PreviousPositionProfileId);
        Assert.Equal(newPositionId, positionEvt.NewPositionProfileId);

        var locationEvt = Assert.Single(publisher.Published.OfType<HR.Modules.Employees.Contracts.EmployeeLocationChangedIntegrationEvent>());
        Assert.Equal(previousLocationId, locationEvt.PreviousLocationId);
        Assert.Equal(newLocationId, locationEvt.NewLocationId);

        var managerEvt = Assert.Single(publisher.Published.OfType<HR.Modules.Employees.Contracts.EmployeeManagerChangedIntegrationEvent>());
        Assert.Null(managerEvt.PreviousManagerId);
        Assert.Equal(manager.Id, managerEvt.NewManagerId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_No_Integration_Events_When_Nothing_Relevant_Changed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = employee.Status,
            StartDate = StartDate
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(publisher.Published);
    }

    // ── employee number correction ───────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Updates_EmployeeNumber_To_Valid_Unused_Value()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            EmployeeNumber = "EMP-9999"
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("EMP-9999", result.Value!.EmployeeNumber);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal("EMP-9999", saved.EmployeeNumber);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_EmployeeNumber_Already_Used_By_Another_Employee_In_Same_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee1 = CreateEmployee(companyId, now);
        var employee2 = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.AddRange(employee1, employee2);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee1.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            EmployeeNumber = employee2.EmployeeNumber
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var saved = await context.Employees.SingleAsync(e => e.Id == employee1.Id);
        Assert.Equal(employee1.EmployeeNumber, saved.EmployeeNumber);
    }

    [Fact]
    public async Task HandleAsync_Allows_Two_Different_Employees_To_Keep_Independent_Numbers()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee1 = CreateEmployee(companyId, now);
        var employee2 = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.AddRange(employee1, employee2);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeClock(FixedUtcNow));

        var result1 = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee1.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            EmployeeNumber = "EMP-1111"
        }, Guid.NewGuid(), CancellationToken.None);

        var result2 = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee2.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            EmployeeNumber = "EMP-2222"
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        var saved1 = await context.Employees.SingleAsync(e => e.Id == employee1.Id);
        var saved2 = await context.Employees.SingleAsync(e => e.Id == employee2.Id);
        Assert.Equal("EMP-1111", saved1.EmployeeNumber);
        Assert.Equal("EMP-2222", saved2.EmployeeNumber);
    }

    [Fact]
    public async Task HandleAsync_Publishes_EmploymentDetailsUpdatedAuditEvent_With_Before_And_After_EmployeeNumber()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, new FakeClock(FixedUtcNow), auditPublisher: auditPublisher);

        var actorId = Guid.NewGuid();

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            EmployeeNumber = "EMP-8888"
        }, actorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.Single(auditPublisher.Published);
        Assert.Equal("employee.employment-details.updated", auditEvent.EventType);
        Assert.Equal(employee.Id, auditEvent.EntityId);
        Assert.Equal(actorId, auditEvent.ActorEmployeeId);

        var before = Assert.IsType<EmploymentDetailsSnapshot>(auditEvent.Before);
        var after = Assert.IsType<EmploymentDetailsSnapshot>(auditEvent.After);
        Assert.Equal("EMP-0001", before.EmployeeNumber);
        Assert.Equal("EMP-8888", after.EmployeeNumber);
    }

    // ── ManagerId audit tracking (Task C) ────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Publishes_EmploymentDetailsUpdatedAuditEvent_With_Before_And_After_ManagerId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var oldManager = CreateEmployee(companyId, now);
        var newManager = CreateEmployee(companyId, now);
        var employee = CreateEmployee(companyId, now);
        employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, oldManager.Id, now);
        context.Employees.AddRange(oldManager, newManager, employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, new FakeClock(FixedUtcNow), auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            ManagerId = newManager.Id
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.Single(auditPublisher.Published);

        var before = Assert.IsType<EmploymentDetailsSnapshot>(auditEvent.Before);
        var after = Assert.IsType<EmploymentDetailsSnapshot>(auditEvent.After);
        Assert.Equal(oldManager.Id, before.ManagerId);
        Assert.Equal(newManager.Id, after.ManagerId);
    }

    // ── CorrelationId propagation (Task D) ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_Passes_Request_CorrelationId_Onto_Published_AuditEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, new FakeClock(FixedUtcNow), auditPublisher: auditPublisher);
        var correlationId = Guid.NewGuid();

        var result = await handler.HandleAsync(new UpdateEmploymentDetailsRequest
        {
            CompanyId = companyId,
            Id = employee.Id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            CorrelationId = correlationId
        }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.Single(auditPublisher.Published);
        Assert.Equal(correlationId, auditEvent.CorrelationId);
    }

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
