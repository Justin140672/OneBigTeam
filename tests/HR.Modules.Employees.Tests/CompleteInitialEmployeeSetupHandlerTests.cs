using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CompleteInitialEmployeeSetup;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CompleteInitialEmployeeSetupHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static FakeCompanyContactValidationReader UkContactRules() => new(
        UkTestRegexPatterns.Postcode, UkTestRegexPatterns.Telephone, UkTestRegexPatterns.Mobile);

    private static CompleteInitialEmployeeSetupRequest ValidRequest() => new()
    {
        FirstName = "Alice",
        LastName = "Smith",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Nationality = "British",
        Gender = "Female",
        AddressLine1 = "1 Test Street",
        City = "London",
        PostCode = "SW1A 1AA"
    };

    private static Employee CreateEmployee(Guid companyId, DateTimeOffset now, bool requiresInitialSetup = true)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Placeholder", "Admin", "admin@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1900, 1, 2), "Unknown", "Unknown", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        if (requiresInitialSetup)
            employee.MarkRequiresInitialSetup(now);

        return employee;
    }

    private static Compensation CreateCompensation(Guid companyId, Guid employeeId, DateTimeOffset now)
        => Compensation.Create(
            Guid.NewGuid(), companyId, employeeId, StartDate, SalaryType.Annual, 0m, "GBP",
            null, null, null, CompensationChangeReason.NewHire, employeeId, now);

    [Fact]
    public async Task HandleAsync_Completes_Setup_And_Activates_Employee_On_Happy_Path()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        context.Compensations.Add(CreateCompensation(companyId, employee.Id, now));
        await context.SaveChangesAsync();

        var workEmail = employee.WorkEmail;
        var startDate = employee.StartDate;

        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(ValidRequest(), companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.RequiresInitialSetup);
        Assert.Equal(EmploymentStatus.Active, result.Value.Status);

        var saved = await context.Employees.SingleAsync();
        Assert.False(saved.RequiresInitialSetup);
        Assert.NotNull(saved.InitialSetupCompletedAt);
        Assert.Equal(EmploymentStatus.Active, saved.Status);
        Assert.Equal("Alice", saved.FirstName);
        Assert.Equal("Smith", saved.LastName);
        Assert.Equal(workEmail, saved.WorkEmail);
        Assert.Equal(startDate, saved.StartDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Employee_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(ValidRequest(), companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Setup_Already_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now, requiresInitialSetup: false);
        context.Employees.Add(employee);
        context.Compensations.Add(CreateCompensation(companyId, employee.Id, now));
        await context.SaveChangesAsync();

        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(ValidRequest(), companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PostCode_Does_Not_Match_Company_Regex()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        context.Compensations.Add(CreateCompensation(companyId, employee.Id, now));
        await context.SaveChangesAsync();

        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            ValidRequest() with { PostCode = "not a postcode" }, companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PhoneNumber_Does_Not_Match_Company_Mobile_Regex()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        context.Compensations.Add(CreateCompensation(companyId, employee.Id, now));
        await context.SaveChangesAsync();

        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            ValidRequest() with { PhoneNumber = "12345" }, companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_HomePhone_Does_Not_Match_Company_Telephone_Regex()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        context.Compensations.Add(CreateCompensation(companyId, employee.Id, now));
        await context.SaveChangesAsync();

        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            ValidRequest() with { HomePhone = "abcdefg" }, companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Employee_Has_No_Compensation_Records()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(ValidRequest(), companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Employee_Has_At_Least_One_Compensation_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        context.Compensations.Add(CreateCompensation(companyId, employee.Id, now));
        await context.SaveChangesAsync();

        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(ValidRequest(), companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_And_Integration_Events_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        context.Compensations.Add(CreateCompensation(companyId, employee.Id, now));
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var integrationPublisher = new CapturingIntegrationEventPublisher();
        var handler = new CompleteInitialEmployeeSetupHandler(
            context, new FakeClock(FixedUtcNow), UkContactRules(), auditPublisher, integrationPublisher);

        var result = await handler.HandleAsync(ValidRequest(), companyId, employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(auditPublisher.Published);
        Assert.Single(integrationPublisher.Published);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
