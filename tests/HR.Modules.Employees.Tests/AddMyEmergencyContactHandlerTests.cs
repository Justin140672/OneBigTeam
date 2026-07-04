using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AddMyEmergencyContact;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class AddMyEmergencyContactHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static FakeCompanyContactValidationReader UkContactRules() => new(
        UkTestRegexPatterns.Postcode, UkTestRegexPatterns.Telephone, UkTestRegexPatterns.Mobile);

    private static AddMyEmergencyContactRequest ValidRequest(Guid companyId) => new()
    {
        CompanyId = companyId,
        Name = "Jane Doe",
        Relationship = "Spouse",
        PhoneNumber = "07700 900000"
    };

    [Fact]
    public async Task HandleAsync_Adds_Emergency_Contact()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new AddMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(ValidRequest(companyId), employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane Doe", result.Value!.Name);
        Assert.Equal("07700 900000", result.Value.PhoneNumber);

        var saved = await context.EmergencyContacts.SingleAsync();
        Assert.Equal(employee.Id, saved.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Employee_Linked_To_User()
    {
        await using var context = BuildContext();
        var handler = new AddMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(ValidRequest(Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Phone_Matches_Neither_Mobile_Nor_Telephone_Regex()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new AddMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId) with { PhoneNumber = "not-a-phone-number" },
            employee.Id,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(await context.EmergencyContacts.ToListAsync());
    }

    [Theory]
    [InlineData("07700 900000")] // mobile format
    [InlineData("01234 567890")] // landline format
    public async Task HandleAsync_Succeeds_When_Phone_Matches_Mobile_Or_Telephone_Regex(string phone)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new AddMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId) with { PhoneNumber = phone },
            employee.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(phone, result.Value!.PhoneNumber);
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
        var handler = new AddMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeCompanyContactValidationReader());

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
