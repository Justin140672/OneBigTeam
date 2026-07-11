using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmployeeProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateEmployeeProfileHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    [Fact]
    public async Task HandleAsync_Updates_Employee_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var updateTime = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(updateTime), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alicia",
                LastName = "Jones",
                WorkEmail = "alicia.jones@example.com",
                PersonalEmail = "alicia@gmail.com",
                StartDate = new DateOnly(2026, 8, 1)
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alicia", result.Value!.FirstName);
        Assert.Equal("Jones", result.Value.LastName);
        Assert.Equal("alicia.jones@example.com", result.Value.WorkEmail);
        Assert.Equal("alicia@gmail.com", result.Value.PersonalEmail);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value.StartDate);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal("Alicia", saved.FirstName);
        Assert.Equal("alicia.jones@example.com", saved.WorkEmail);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_Before_And_After_Snapshots()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        employee.UpdatePersonalDetails(null, null, null, "Female", null, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = new UpdateEmployeeProfileHandler(
            context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), auditPublisher);

        var actorId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alicia",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                Gender = "Male"
            },
            actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(auditPublisher.Published);

        var auditEvent = auditPublisher.Published[0];
        Assert.Equal("employee.profile.updated", auditEvent.EventType);
        Assert.Equal(employee.Id, auditEvent.EntityId);
        Assert.Equal(actorId, auditEvent.ActorEmployeeId);

        var before = Assert.IsType<EmployeeProfileSnapshot>(auditEvent.Before);
        var after = Assert.IsType<EmployeeProfileSnapshot>(auditEvent.After);
        Assert.Equal("Alice", before.FirstName);
        Assert.Equal("Female", before.Gender);
        Assert.Equal("Alicia", after.FirstName);
        Assert.Equal("Male", after.Gender);
    }

    [Fact]
    public async Task HandleAsync_Persists_LocationId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, now);
        context.Employees.Add(employee);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                LocationId = location.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(location.Id, result.Value!.LocationId);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal(location.Id, saved.LocationId);
    }

    [Fact]
    public async Task HandleAsync_Normalises_WorkEmail_To_Lowercase()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "Alice.SMITH@EXAMPLE.COM",
                StartDate = StartDate
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice.smith@example.com", result.Value!.WorkEmail);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = Guid.NewGuid(),
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_WorkEmail_Already_Taken_By_Another_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var emp1 = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        var emp2 = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.AddRange(emp1, emp2);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = emp1.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "bob@example.com",  // taken by emp2
                StartDate = StartDate
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Employee_To_Keep_Own_WorkEmail()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alicia",
                LastName = "Smith",
                WorkEmail = "alice@example.com",  // same email, same employee
                StartDate = StartDate
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alicia", result.Value!.FirstName);
    }

    [Fact]
    public async Task HandleAsync_Updates_HasSystemAccess_To_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                HasSystemAccess = false
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasSystemAccess);

        var saved = await context.Employees.SingleAsync();
        Assert.False(saved.HasSystemAccess);
    }

    [Fact]
    public async Task HandleAsync_Preserves_HasSystemAccess_True_When_Not_Changed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                HasSystemAccess = true
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.HasSystemAccess);
    }

    private static FakeCompanyContactValidationReader UkContactRules() => new(
        UkTestRegexPatterns.Postcode, UkTestRegexPatterns.Telephone, UkTestRegexPatterns.Mobile);

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PostCode_Does_Not_Match_Company_Regex()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                PostCode = "not a postcode"
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PhoneNumber_Does_Not_Match_Company_Mobile_Regex()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                PhoneNumber = "12345"
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_HomePhone_Does_Not_Match_Company_Telephone_Regex()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                HomePhone = "abcdefg"
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Contact_Fields_Are_Valid_UK_Formats()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateEmployeeProfileHandler(context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new UpdateEmployeeProfileRequest
            {
                CompanyId = companyId,
                Id = employee.Id,
                FirstName = "Alice",
                LastName = "Smith",
                WorkEmail = "alice@example.com",
                StartDate = StartDate,
                PostCode = "M1 1AE",
                PhoneNumber = "07700 900123",
                HomePhone = "01234 567890"
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal("M1 1AE", saved.PostCode);
        Assert.Equal("07700 900123", saved.PhoneNumber);
        Assert.Equal("01234 567890", saved.HomePhone);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
