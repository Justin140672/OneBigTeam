using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateMyContactDetails;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateMyContactDetailsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static FakeCompanyContactValidationReader UkContactRules() => new(
        UkTestRegexPatterns.Postcode, UkTestRegexPatterns.Telephone, UkTestRegexPatterns.Mobile);

    private static UpdateMyContactDetailsRequest ValidRequest(Guid companyId) => new()
    {
        CompanyId = companyId,
        AddressLine1 = "1 Test Street",
        City = "London",
        PostCode = "SW1A 1AA",
        Country = "United Kingdom"
    };

    [Fact]
    public async Task HandleAsync_Updates_Contact_Details()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateMyContactDetailsHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            ValidRequest(companyId) with { PersonalEmail = "alice.personal@example.com" },
            employee.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice.personal@example.com", result.Value!.PersonalEmail);
        Assert.Equal("SW1A 1AA", result.Value.PostCode);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal("SW1A 1AA", saved.PostCode);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Employee_Linked_To_User()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var handler = new UpdateMyContactDetailsHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(ValidRequest(companyId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_PostCode_Does_Not_Match_Company_Regex()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateMyContactDetailsHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId) with { PostCode = "not a postcode" },
            employee.Id,
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

        var handler = new UpdateMyContactDetailsHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId) with { PhoneNumber = "12345" },
            employee.Id,
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

        var handler = new UpdateMyContactDetailsHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId) with { HomePhone = "abcdefg" },
            employee.Id,
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

        var handler = new UpdateMyContactDetailsHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId) with
            {
                PostCode = "M1 1AE",
                PhoneNumber = "07700 900123",
                HomePhone = "01234 567890"
            },
            employee.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("M1 1AE", result.Value!.PostCode);
        Assert.Equal("07700 900123", result.Value.PhoneNumber);
        Assert.Equal("01234 567890", result.Value.HomePhone);
    }

    [Fact]
    public async Task HandleAsync_Skips_Contact_Validation_When_Phone_Fields_Are_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new UpdateMyContactDetailsHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId) with { PhoneNumber = null, HomePhone = null },
            employee.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_On_Success()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new UpdateMyContactDetailsHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(ValidRequest(companyId), employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(publisher.Published);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
