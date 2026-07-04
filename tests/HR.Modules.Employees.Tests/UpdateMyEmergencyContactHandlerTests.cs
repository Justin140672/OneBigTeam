using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateMyEmergencyContact;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateMyEmergencyContactHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static FakeCompanyContactValidationReader UkContactRules() => new(
        UkTestRegexPatterns.Postcode, UkTestRegexPatterns.Telephone, UkTestRegexPatterns.Mobile);

    private static UpdateMyEmergencyContactRequest ValidRequest(Guid companyId, Guid contactId) => new()
    {
        CompanyId = companyId,
        ContactId = contactId,
        Name = "Jane Doe",
        Relationship = "Spouse",
        PhoneNumber = "07700 900000"
    };

    private static async Task<(EmployeesDbContext Context, Guid CompanyId, Guid EmployeeId, Guid ContactId)> SeedAsync()
    {
        var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, now);
        context.Employees.Add(employee);

        var contact = EmergencyContact.Create(
            Guid.NewGuid(), employee.Id, companyId, "Original Name", "Parent", "01234 000000", null, now);
        context.EmergencyContacts.Add(contact);

        await context.SaveChangesAsync();

        return (context, companyId, employee.Id, contact.Id);
    }

    [Fact]
    public async Task HandleAsync_Updates_Emergency_Contact()
    {
        var (context, companyId, employeeId, contactId) = await SeedAsync();
        var handler = new UpdateMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(ValidRequest(companyId, contactId), employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane Doe", result.Value!.Name);
        Assert.Equal("07700 900000", result.Value.PhoneNumber);

        var saved = await context.EmergencyContacts.SingleAsync();
        Assert.Equal("Jane Doe", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Contact_Does_Not_Exist()
    {
        var (context, companyId, employeeId, _) = await SeedAsync();
        var handler = new UpdateMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(ValidRequest(companyId, Guid.NewGuid()), employeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Phone_Matches_Neither_Mobile_Nor_Telephone_Regex()
    {
        var (context, companyId, employeeId, contactId) = await SeedAsync();
        var handler = new UpdateMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId, contactId) with { PhoneNumber = "not-a-phone-number" },
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        // The original contact should be untouched since validation ran before Update().
        var saved = await context.EmergencyContacts.SingleAsync();
        Assert.Equal("Original Name", saved.Name);
    }

    [Theory]
    [InlineData("07700 900000")] // mobile format
    [InlineData("01234 567890")] // landline format
    public async Task HandleAsync_Succeeds_When_Phone_Matches_Mobile_Or_Telephone_Regex(string phone)
    {
        var (context, companyId, employeeId, contactId) = await SeedAsync();
        var handler = new UpdateMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), UkContactRules());

        var result = await handler.HandleAsync(
            ValidRequest(companyId, contactId) with { PhoneNumber = phone },
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(phone, result.Value!.PhoneNumber);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_On_Success()
    {
        var (context, companyId, employeeId, contactId) = await SeedAsync();
        var publisher = new FakeAuditPublisher();
        var handler = new UpdateMyEmergencyContactHandler(context, new FakeClock(FixedUtcNow), publisher, new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(ValidRequest(companyId, contactId), employeeId, CancellationToken.None);

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
