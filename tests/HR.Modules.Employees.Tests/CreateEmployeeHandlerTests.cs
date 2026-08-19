using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;


public class CreateEmployeeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_Draft_Status()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice.smith@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Alice", result.Value.FirstName);
        Assert.Equal("Smith", result.Value.LastName);
        Assert.Equal("alice.smith@example.com", result.Value.WorkEmail);
        Assert.Equal(StartDate, result.Value.StartDate);
        Assert.Equal(EmploymentStatus.Draft, result.Value.Status);
        Assert.Equal(departmentId, result.Value.DepartmentId);
        Assert.Equal(positionProfileId, result.Value.PositionProfileId);
        Assert.Null(result.Value.ManagerId);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Normalises_WorkEmail_To_Lowercase()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Bob",
                LastName = "Jones",
                WorkEmail = "Bob.Jones@EXAMPLE.COM",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Male"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("bob.jones@example.com", result.Value!.WorkEmail);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_Department_And_PositionProfile_And_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var positionProfile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, Guid.NewGuid(), "Developer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane.manager@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Departments.Add(department);
        context.PositionProfiles.Add(positionProfile);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var (_, locationId, _, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                LocationId = locationId,
                PositionProfileId = positionProfile.Id,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0002",
                ManagerId = manager.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice.smith@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(department.Id, result.Value!.DepartmentId);
        Assert.Equal(positionProfile.Id, result.Value.PositionProfileId);
        Assert.Equal(manager.Id, result.Value.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_Location()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, now);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var (departmentId, _, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = location.Id,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice.smith@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(location.Id, result.Value!.LocationId);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal(location.Id, saved.LocationId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Location_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                LocationId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                EmployeeNumber = "EMP-TEST"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Location_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var otherCompanyId = Guid.NewGuid();
        var locationType = LocationType.Create(Guid.NewGuid(), otherCompanyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), otherCompanyId, locationType.Id, "Head Office", null, now);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                LocationId = location.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                EmployeeNumber = "EMP-TEST"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_WorkEmail_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.Add(Employee.Create(Guid.NewGuid(), companyId, "Existing", "User", "alice.smith@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now));
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice.smith@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_WorkEmail_In_Different_Companies()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.Add(Employee.Create(Guid.NewGuid(), companyA, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now));
        await context.SaveChangesAsync();

        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyB, now);

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyB,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                DepartmentId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                EmployeeNumber = "EMP-TEST"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var department = Department.Create(Guid.NewGuid(), Guid.NewGuid(), "Engineering", null, now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                DepartmentId = department.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                EmployeeNumber = "EMP-TEST"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                EmployeeNumber = "EMP-TEST"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Developer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = profile.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                EmployeeNumber = "EMP-TEST"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Manager_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                ManagerId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                EmployeeNumber = "EMP-TEST"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Manager_Is_Terminated()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        manager.SetStatusForTesting(EmploymentStatus.FormerEmployee, now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                ManagerId = manager.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                EmployeeNumber = "EMP-TEST"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_HasSystemAccess_True_By_Default()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.HasSystemAccess);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_HasSystemAccess_False_When_Specified()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female",
                HasSystemAccess = false
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasSystemAccess);

        var saved = await context.Employees.SingleAsync();
        Assert.False(saved.HasSystemAccess);
    }

    [Fact]
    public async Task HandleAsync_Sets_PreferredName_To_FirstName_When_Not_Provided()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal("Alice", saved.PreferredName);
    }

    [Fact]
    public async Task HandleAsync_Uses_Provided_PreferredName_When_Supplied()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                PreferredName = "Al",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal("Al", saved.PreferredName);
        Assert.Equal(new DateOnly(1990, 5, 20), saved.DateOfBirth);
        Assert.Equal("British", saved.Nationality);
        Assert.Equal("Female", saved.Gender);
    }

    [Fact]
    public async Task HandleAsync_Publishes_EmployeeCreatedIntegrationEvent_On_Success()
    {
        await using var context = BuildContext();
        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female",
                HasSystemAccess = true
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(publisher.Published);
        var created = Assert.IsType<EmployeeCreatedIntegrationEvent>(evt);
        Assert.Equal(companyId, created.CompanyId);
        Assert.Equal(result.Value!.Id, created.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Event_When_Creation_Fails()
    {
        await using var context = BuildContext();
        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();

        // seed a conflicting employee so creation fails
        var existing = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "alice@example.com", StartDate, true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        context.Employees.Add(existing);
        await context.SaveChangesAsync();

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                HasSystemAccess = true
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Sets_ProbationEndDate_Using_Company_Default_When_No_Position_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        // The seeded position profile below has no ProbationMonthsOverride, so the resolver's
        // company default still applies (this only exercises the "no override present" path,
        // not a literal absence of PositionProfile — that is no longer a valid Employee state).
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);
        var reader = new FakeProbationDateResolver(months: 6);
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), reader, new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(StartDate.AddMonths(6), saved.ProbationEndDate);
    }

    [Fact]
    public async Task HandleAsync_Sets_ProbationEndDate_Using_PositionProfile_Override_When_Present()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Senior Dev", null, 3, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var (departmentId, locationId, _, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var reader = new FakeProbationDateResolver(months: 6);
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), reader, new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = profile.Id,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal(StartDate.AddMonths(3), saved.ProbationEndDate);
    }

    [Fact]
    public async Task HandleAsync_Published_Event_Includes_Resolved_ProbationEndDate()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);
        var publisher = new CapturingIntegrationEventPublisher();
        var reader = new FakeProbationDateResolver(months: 9);
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, reader, new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<EmployeeCreatedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(StartDate.AddMonths(9), evt.ProbationEndDate);
    }

    [Fact]
    public async Task HandleAsync_Published_Event_Includes_PositionProfile_DefaultLeavePolicyId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var leavePolicyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Senior Dev", null, null, null, null, null, null, null, leavePolicyId, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var (departmentId, locationId, _, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = profile.Id,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<EmployeeCreatedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(leavePolicyId, evt.DefaultLeavePolicyId);
    }

    [Fact]
    public async Task HandleAsync_Published_Event_Has_PositionProfiles_DefaultLeavePolicyId()
    {
        // DefaultLeavePolicyId is now mandatory on PositionProfile (a PositionProfile can no longer
        // exist without one), so every employee linked to a position profile now always publishes a
        // non-null DefaultLeavePolicyId on the created event. This supersedes the old
        // "...Has_Null_DefaultLeavePolicyId_When_No_PositionProfile" scenario, which is no longer a
        // reachable state.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);
        var expectedLeavePolicyId = (await context.PositionProfiles.SingleAsync(p => p.Id == positionProfileId)).DefaultLeavePolicyId;
        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<EmployeeCreatedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(expectedLeavePolicyId, evt.DefaultLeavePolicyId);
    }

    private static FakeCompanyContactValidationReader UkContactRules() => new(
        UkTestRegexPatterns.Postcode, UkTestRegexPatterns.Telephone, UkTestRegexPatterns.Mobile);

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PostCode_Does_Not_Match_Company_Regex()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                PostCode = "not a postcode"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(await context.Employees.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PhoneNumber_Does_Not_Match_Company_Mobile_Regex()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                PhoneNumber = "12345"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_HomePhone_Does_Not_Match_Company_Telephone_Regex()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                HomePhone = "abcdefg"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Contact_Fields_Are_Valid_UK_Formats()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female",
                PostCode = "SW1A 1AA",
                PhoneNumber = "07700 900000",
                HomePhone = "01234 567890"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal("SW1A 1AA", saved.PostCode);
        Assert.Equal("07700 900000", saved.PhoneNumber);
        Assert.Equal("01234 567890", saved.HomePhone);
    }

    [Fact]
    public async Task HandleAsync_Skips_Contact_Validation_When_Fields_Are_Null_Or_Empty()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-0001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female",
                PostCode = null,
                PhoneNumber = null,
                HomePhone = null
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Manual_Mode_And_EmployeeNumber_Is_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(),
            new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual),
            new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = string.Empty,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("Employee number is required.", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Generates_EmployeeNumber_When_Automatic_Mode_And_EmployeeNumber_Is_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(),
            new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic),
            new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = string.Empty,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal("AUTO-00001", saved.EmployeeNumber);
    }

    [Fact]
    public async Task HandleAsync_Retries_When_Generated_EmployeeNumber_Already_Exists()
    {
        // The atomic counter itself is race-free (see EmployeeNumberGenerator's own remarks), but
        // its stored "next" value can still drift out of sync with actual data by means outside
        // CreateEmployeeHandler's control — e.g. an admin directly editing "Next Number" on HR
        // Settings to a value at or behind one already claimed. The handler must retry with a
        // fresh claim rather than failing the request outright.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        var existing = Employee.Create(
            Guid.NewGuid(), companyId, "Existing", "Employee", "existing@example.com", StartDate,
            hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Female", "AUTO-00001",
            employmentTypeId, departmentId, locationId, positionProfileId, now);
        context.Employees.Add(existing);
        await context.SaveChangesAsync();

        // First claim ("AUTO-00001") collides with the employee just seeded; the second
        // ("AUTO-00002") does not.
        var generator = new FakeEmployeeNumberGenerator(n => $"AUTO-{n:D5}");
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(),
            new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic),
            generator);

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = string.Empty,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync(e => e.Id == result.Value!.Id);
        Assert.Equal("AUTO-00002", saved.EmployeeNumber);
    }

    [Fact]
    public async Task HandleAsync_Uses_Supplied_EmployeeNumber_When_Automatic_Mode_And_Value_Provided()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(),
            new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic),
            new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "MANUAL-001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal("MANUAL-001", saved.EmployeeNumber);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_For_Case_Insensitive_Duplicate_EmployeeNumber_In_Same_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyId, now);

        context.Employees.Add(Employee.Create(
            Guid.NewGuid(), companyId, "Existing", "User", "existing@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-001",
            employmentTypeId, departmentId, locationId, positionProfileId, now));
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(),
            new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "emp-001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_EmployeeNumber_In_Different_Companies()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (deptA, locA, ppA, etA) = await SeedMandatoryLookupsAsync(context, companyA, now);
        context.Employees.Add(Employee.Create(
            Guid.NewGuid(), companyA, "Existing", "User", "existing@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-001",
            etA, deptA, locA, ppA, now));
        await context.SaveChangesAsync();

        var (departmentId, locationId, positionProfileId, employmentTypeId) = await SeedMandatoryLookupsAsync(context, companyB, now);

        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(),
            new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyB,
                DepartmentId = departmentId,
                LocationId = locationId,
                PositionProfileId = positionProfileId,
                EmploymentTypeId = employmentTypeId,
                EmployeeNumber = "EMP-001",
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                DateOfBirth = new DateOnly(1990, 5, 20),
                Nationality = "British",
                Gender = "Female"
            },
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

    /// <summary>
    /// Seeds an active Department, Location, PositionProfile and EmploymentType for
    /// <paramref name="companyId"/> and returns their ids. CreateEmployeeRequest requires all
    /// four (plus DateOfBirth/Nationality/Gender/EmployeeNumber) to resolve to a real, active,
    /// same-company row, so most handler tests need this fixture.
    /// </summary>
    private static async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)> SeedMandatoryLookupsAsync(
        EmployeesDbContext context, Guid companyId, DateTimeOffset now)
    {
        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, now);
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, department.Id, location.Id, "Developer",
            null, null, null, null, null, null, null, Guid.NewGuid(), now);
        var employmentType = EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, now);

        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.Add(positionProfile);
        context.EmploymentTypes.Add(employmentType);
        await context.SaveChangesAsync();

        return (department.Id, location.Id, positionProfile.Id, employmentType.Id);
    }
}
