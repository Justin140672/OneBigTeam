using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class EmployeeServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private static CreateEmployeeRequest SampleCreateRequest() => new(
        CompanyId: Guid.NewGuid(),
        DepartmentId: null,
        LocationId: null,
        PositionProfileId: null,
        FirstName: "Alice",
        LastName: "Smith",
        PreferredName: null,
        WorkEmail: "alice@example.com",
        PersonalEmail: null,
        StartDate: DateOnly.FromDateTime(DateTime.Today),
        DateOfBirth: DateOnly.FromDateTime(DateTime.Today).AddYears(-30),
        Nationality: "British",
        Gender: "Female",
        GenderOther: null,
        PhoneNumber: null,
        HomePhone: null,
        AddressLine1: null,
        AddressLine2: null,
        City: null,
        County: null,
        PostCode: null,
        Country: null,
        HasSystemAccess: true);

    private static UpdateEmployeeProfileRequest SampleUpdateRequest() => new(
        CompanyId: Guid.NewGuid(),
        Id: Guid.NewGuid(),
        DepartmentId: null,
        LocationId: null,
        PositionProfileId: null,
        FirstName: "Alice",
        LastName: "Smith",
        PreferredName: null,
        WorkEmail: "alice@example.com",
        PersonalEmail: null,
        StartDate: DateOnly.FromDateTime(DateTime.Today),
        DateOfBirth: null,
        Nationality: null,
        Gender: null,
        GenderOther: null,
        PhoneNumber: null,
        HomePhone: null,
        AddressLine1: null,
        AddressLine2: null,
        City: null,
        County: null,
        PostCode: null,
        Country: null,
        HasSystemAccess: true,
        WorkingDaysOverride: null,
        HoursPerDayOverride: null);

    // ── CreateEmployeeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEmployeeAsync_Returns_Employee_When_Api_Returns_Created()
    {
        var response = new CreateEmployeeResponse(
            Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", "Draft", DateTimeOffset.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Created, response));
        var service = new EmployeeService(factory);

        var (created, error) = await service.CreateEmployeeAsync(Guid.NewGuid(), SampleCreateRequest());

        Assert.NotNull(created);
        Assert.Null(error);
        Assert.Equal("Alice", created!.FirstName);
    }

    [Fact]
    public async Task CreateEmployeeAsync_Returns_ConflictMessage_When_Api_Returns_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "An employee with work email 'alice@example.com' already exists in this company." }));
        var service = new EmployeeService(factory);

        var (created, error) = await service.CreateEmployeeAsync(Guid.NewGuid(), SampleCreateRequest());

        Assert.Null(created);
        Assert.Equal("An employee with work email 'alice@example.com' already exists in this company.", error);
    }

    [Fact]
    public async Task CreateEmployeeAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        // This is the new Handler-level regex validation failure path (postcode/mobile/telephone),
        // which surfaces as 400 BadRequest with a { error } body rather than 422.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "'12345' is not a valid mobile number." }));
        var service = new EmployeeService(factory);

        var (created, error) = await service.CreateEmployeeAsync(Guid.NewGuid(), SampleCreateRequest());

        Assert.Null(created);
        Assert.Equal("'12345' is not a valid mobile number.", error);
    }

    [Fact]
    public async Task CreateEmployeeAsync_Returns_GenericMessage_When_Api_Returns_Unexpected_Error()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.InternalServerError, new { }));
        var service = new EmployeeService(factory);

        var (created, error) = await service.CreateEmployeeAsync(Guid.NewGuid(), SampleCreateRequest());

        Assert.Null(created);
        Assert.Equal("Failed to create employee.", error);
    }

    // ── UpdateEmployeeProfileAsync ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateEmployeeProfileAsync_Returns_Success_When_Api_Returns_Ok()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, new { }));
        var service = new EmployeeService(factory);

        var (success, error) = await service.UpdateEmployeeProfileAsync(Guid.NewGuid(), Guid.NewGuid(), SampleUpdateRequest());

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public async Task UpdateEmployeeProfileAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "'not a postcode' is not a valid postcode." }));
        var service = new EmployeeService(factory);

        var (success, error) = await service.UpdateEmployeeProfileAsync(Guid.NewGuid(), Guid.NewGuid(), SampleUpdateRequest());

        Assert.False(success);
        Assert.Equal("'not a postcode' is not a valid postcode.", error);
    }

    [Fact]
    public async Task UpdateEmployeeProfileAsync_Returns_ConflictMessage_When_Api_Returns_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "A conflict occurred." }));
        var service = new EmployeeService(factory);

        var (success, error) = await service.UpdateEmployeeProfileAsync(Guid.NewGuid(), Guid.NewGuid(), SampleUpdateRequest());

        Assert.False(success);
        Assert.Equal("A conflict occurred.", error);
    }

    // ── UpdateMyContactDetailsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateMyContactDetailsAsync_Returns_Success_When_Api_Returns_Ok()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, new { }));
        var service = new EmployeeService(factory);

        var (success, error) = await service.UpdateMyContactDetailsAsync(
            Guid.NewGuid(),
            new UpdateMyContactDetailsRequest(Guid.NewGuid(), null, null, null, "1 Test Street", null, "London", null, "SW1A 1AA", "United Kingdom"));

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public async Task UpdateMyContactDetailsAsync_Returns_FirstError_When_Api_Returns_UnprocessableEntity()
    {
        var errors = new Dictionary<string, string[]> { ["PostCode"] = ["Post code is required."] };
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.UnprocessableEntity, new { errors }));
        var service = new EmployeeService(factory);

        var (success, error) = await service.UpdateMyContactDetailsAsync(
            Guid.NewGuid(),
            new UpdateMyContactDetailsRequest(Guid.NewGuid(), null, null, null, "1 Test Street", null, "London", null, "", "United Kingdom"));

        Assert.False(success);
        Assert.Equal("Post code is required.", error);
    }

    [Fact]
    public async Task UpdateMyContactDetailsAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "'12345' is not a valid mobile number." }));
        var service = new EmployeeService(factory);

        var (success, error) = await service.UpdateMyContactDetailsAsync(
            Guid.NewGuid(),
            new UpdateMyContactDetailsRequest(Guid.NewGuid(), null, "12345", null, "1 Test Street", null, "London", null, "SW1A 1AA", "United Kingdom"));

        Assert.False(success);
        Assert.Equal("'12345' is not a valid mobile number.", error);
    }

    [Fact]
    public async Task UpdateMyContactDetailsAsync_Returns_GenericError_When_Network_Fails()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new EmployeeService(factory);

        var (success, error) = await service.UpdateMyContactDetailsAsync(
            Guid.NewGuid(),
            new UpdateMyContactDetailsRequest(Guid.NewGuid(), null, null, null, "1 Test Street", null, "London", null, "SW1A 1AA", "United Kingdom"));

        Assert.False(success);
        Assert.Equal("An unexpected error occurred.", error);
    }

    // ── AddMyEmergencyContactAsync ───────────────────────────────────────────────

    [Fact]
    public async Task AddMyEmergencyContactAsync_Returns_Contact_When_Api_Returns_Created()
    {
        var contact = new EmergencyContactItem(Guid.NewGuid(), "Jane Doe", "Spouse", "07700 900000", null);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Created, contact));
        var service = new EmployeeService(factory);

        var (created, error) = await service.AddMyEmergencyContactAsync(
            Guid.NewGuid(),
            new AddEmergencyContactRequest(Guid.NewGuid(), "Jane Doe", "Spouse", "07700 900000", null));

        Assert.NotNull(created);
        Assert.Null(error);
        Assert.Equal("Jane Doe", created!.Name);
    }

    [Fact]
    public async Task AddMyEmergencyContactAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        // The new mobile-OR-telephone regex validation failure surfaces this way.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "'not-a-phone' is not a valid phone number." }));
        var service = new EmployeeService(factory);

        var (created, error) = await service.AddMyEmergencyContactAsync(
            Guid.NewGuid(),
            new AddEmergencyContactRequest(Guid.NewGuid(), "Jane Doe", "Spouse", "not-a-phone", null));

        Assert.Null(created);
        Assert.Equal("'not-a-phone' is not a valid phone number.", error);
    }

    // ── GetEmployeeAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEmployeeAsync_Returns_Null_When_Network_Fails()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new EmployeeService(factory);

        var result = await service.GetEmployeeAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, object payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }
}
