using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
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
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());
        var companyId = Guid.NewGuid();

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

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Alice", result.Value.FirstName);
        Assert.Equal("Smith", result.Value.LastName);
        Assert.Equal("alice.smith@example.com", result.Value.WorkEmail);
        Assert.Equal(StartDate, result.Value.StartDate);
        Assert.Equal(EmploymentStatus.Draft, result.Value.Status);
        Assert.Null(result.Value.DepartmentId);
        Assert.Null(result.Value.PositionProfileId);
        Assert.Null(result.Value.ManagerId);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Normalises_WorkEmail_To_Lowercase()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Bob",
                LastName = "Jones",
                WorkEmail = "Bob.Jones@EXAMPLE.COM",
                StartDate = StartDate
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
        var positionProfile = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, "Developer", null, false, null, null, null, null, null, null, now);
        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane.manager@example.com", StartDate, hasSystemAccess: true, now);
        context.Departments.Add(department);
        context.PositionProfiles.Add(positionProfile);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                DepartmentId = department.Id,
                PositionProfileId = positionProfile.Id,
                ManagerId = manager.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice.smith@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(department.Id, result.Value!.DepartmentId);
        Assert.Equal(positionProfile.Id, result.Value.PositionProfileId);
        Assert.Equal(manager.Id, result.Value.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_WorkEmail_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.Employees.Add(Employee.Create(Guid.NewGuid(), companyId, "Existing", "User", "alice.smith@example.com", StartDate, hasSystemAccess: true, now));
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

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

        context.Employees.Add(Employee.Create(Guid.NewGuid(), companyA, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now));
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyB,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                DepartmentId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
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

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                DepartmentId = department.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
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

        var profile = PositionProfile.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Developer", null, false, null, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                PositionProfileId = profile.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Manager_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                ManagerId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
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

        var manager = Employee.Create(Guid.NewGuid(), companyId, "Jane", "Manager", "jane@example.com", StartDate, hasSystemAccess: true, now);
        manager.Terminate(now);
        context.Employees.Add(manager);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                ManagerId = manager.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_HasSystemAccess_True_By_Default()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.HasSystemAccess);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_With_HasSystemAccess_False_When_Specified()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
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
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
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
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
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
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());
        var companyId = Guid.NewGuid();

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
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());
        var companyId = Guid.NewGuid();

        // seed a conflicting employee so creation fails
        var existing = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones",
            "alice@example.com", StartDate, true, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
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
        var reader = new FakeProbationDateResolver(months: 6);
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), reader, new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
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

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Senior Dev", null, false, 3, null, null, null, null, null, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var reader = new FakeProbationDateResolver(months: 6);
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), reader, new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
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
        var publisher = new CapturingIntegrationEventPublisher();
        var reader = new FakeProbationDateResolver(months: 9);
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, reader, new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
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

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, "Senior Dev", null, false, null, null, null, null, null, leavePolicyId, now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                PositionProfileId = profile.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<EmployeeCreatedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(leavePolicyId, evt.DefaultLeavePolicyId);
    }

    [Fact]
    public async Task HandleAsync_Published_Event_Has_Null_DefaultLeavePolicyId_When_No_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new CreateEmployeeHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = companyId,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<EmployeeCreatedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Null(evt.DefaultLeavePolicyId);
    }

    private static FakeCompanyContactValidationReader UkContactRules() => new(
        UkTestRegexPatterns.Postcode, UkTestRegexPatterns.Telephone, UkTestRegexPatterns.Mobile);

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PostCode_Does_Not_Match_Company_Regex()
    {
        await using var context = BuildContext();
        var handler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules());

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
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules());

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
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules());

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
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
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
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeProbationDateResolver(), UkContactRules());

        var result = await handler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                PostCode = null,
                PhoneNumber = null,
                HomePhone = null
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
}
