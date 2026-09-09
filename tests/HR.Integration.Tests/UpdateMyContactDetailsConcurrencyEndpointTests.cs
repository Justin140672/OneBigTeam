using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2: optimistic-concurrency coverage for the self-service "My contact details" workflow.
// Also pins that the endpoint now maps Error.Concurrency -> HTTP 409 (previously it only mapped
// 404/400).
[Collection("Integration")]
public class UpdateMyContactDetailsConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid Admin1 = new("ceef0000-0000-0000-0000-000000000001");
    private static readonly Guid Admin2 = new("ceef0000-0000-0000-0000-000000000002");
    private static readonly Guid Admin3 = new("ceef0000-0000-0000-0000-000000000003");
    private static readonly Guid Admin4 = new("ceef0000-0000-0000-0000-000000000004");
    private static readonly Guid Admin5 = new("ceef0000-0000-0000-0000-000000000005");
    private static readonly Guid Admin6 = new("ceef0000-0000-0000-0000-000000000006");

    public UpdateMyContactDetailsConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var u in new[] { Admin1, Admin2, Admin3, Admin4, Admin5, Admin6 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.HrAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.Employee);
            }
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Contact_Details_With_Stale_ExpectedVersion_Returns_409_Concurrency_Not_400_Or_404()
    {
        var ctx = await CreateEmployeeAsync(Admin1);

        var version = (await GetContactDetailsAsync(ctx.EmployeeClient, ctx.CompanyId)).Version;

        var first = await ctx.EmployeeClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/me/contact-details",
            ContactBody(city: "FirstCity", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await ctx.EmployeeClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/me/contact-details",
            ContactBody(city: "StaleCity", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);
    }

    [Fact]
    public async Task Admin_Profile_Edit_Then_Employee_Stale_Self_Service_Save_Returns_409_Concurrency()
    {
        var ctx = await CreateEmployeeAsync(Admin2);

        var version = (await GetContactDetailsAsync(ctx.EmployeeClient, ctx.CompanyId)).Version;

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ctx.AdminUserId.ToString());
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, ctx.CompanyId.ToString());

        var adminEdit = await adminClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/{ctx.EmployeeId}/profile",
            new
            {
                companyId = ctx.CompanyId,
                id = ctx.EmployeeId,
                firstName = "AdminEdited",
                lastName = "Employee",
                workEmail = $"admin.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-15",
                expectedVersion = version
            });
        Assert.Equal(HttpStatusCode.OK, adminEdit.StatusCode);

        var employeeSave = await ctx.EmployeeClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/me/contact-details",
            ContactBody(city: "EmployeeCity", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, employeeSave.StatusCode);
        var body = await employeeSave.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);
    }

    [Fact]
    public async Task Put_Contact_Details_Happy_Path_Bumps_Version()
    {
        var ctx = await CreateEmployeeAsync(Admin3);

        var version = (await GetContactDetailsAsync(ctx.EmployeeClient, ctx.CompanyId)).Version;

        var response = await ctx.EmployeeClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/me/contact-details",
            ContactBody(city: "London", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ContactDetailsPayload>();
        Assert.Equal(version + 1, payload!.Version);

        var reloaded = await GetContactDetailsAsync(ctx.EmployeeClient, ctx.CompanyId);
        Assert.Equal(version + 1, reloaded.Version);

        // A follow-up save with the fresh version also succeeds (no false positives).
        var next = await ctx.EmployeeClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/me/contact-details",
            ContactBody(city: "Manchester", expectedVersion: payload.Version));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Put_Contact_Details_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var ctx = await CreateEmployeeAsync(Admin4);

        var before = await GetContactDetailsAsync(ctx.EmployeeClient, ctx.CompanyId);

        var r1 = await ctx.EmployeeClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/me/contact-details",
            ContactBody(city: "City1", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetContactDetailsAsync(ctx.EmployeeClient, ctx.CompanyId);
        Assert.Equal(before.Version, after.Version);
        Assert.Null(after.City);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/me/contact-details",
            ContactBody(city: "London", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_404_When_No_Employee_Record_Linked_To_User()
    {
        var ctx = await CreateEmployeeAsync(Admin5);

        // Admin6 has the employee role globally but no employee record in this company.
        using var strangerClient = _factory.CreateClient();
        strangerClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Admin6.ToString());
        strangerClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, ctx.CompanyId.ToString());

        var response = await strangerClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/me/contact-details",
            ContactBody(city: "Nowhere", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("not_found", body!.Code);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_400_For_Validation_Failure()
    {
        var ctx = await CreateEmployeeAsync(Admin6);

        var response = await ctx.EmployeeClient.PutAsJsonAsync(
            $"/api/companies/{ctx.CompanyId}/employees/me/contact-details",
            new { addressLine1 = "1 Test Street", city = "", postCode = "SW1A 1AA", country = "United Kingdom", expectedVersion = (int?)null });

        // FastEndpoints request-validation failures surface as 422, not 400.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static object ContactBody(string city, int? expectedVersion)
        => new
        {
            addressLine1 = "1 Test Street",
            city,
            postCode = "SW1A 1AA",
            country = "United Kingdom",
            expectedVersion
        };

    private async Task<EmployeeContext> CreateEmployeeAsync(Guid adminUserId)
    {
        var companyId = Guid.NewGuid();

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, adminUserId.ToString());
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, adminUserId, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(adminClient, companyId);
        var createResponse = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Test", "Employee", $"test.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2026, 1, 15), gender: "Male"));
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<EmployeeRef>())!;

        await TestRoleSeeder.AssignRoleAsync(_factory, created.Id, SystemRoles.Employee);

        var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, created.Id.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        return new EmployeeContext(employeeClient, companyId, created.Id, adminUserId);
    }

    private static async Task<ContactDetailsPayload> GetContactDetailsAsync(HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/me/contact-details");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ContactDetailsPayload>())!;
    }

    private sealed record EmployeeContext(HttpClient EmployeeClient, Guid CompanyId, Guid EmployeeId, Guid AdminUserId);
    private sealed record EmployeeRef(Guid Id);
    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record ContactDetailsPayload(string WorkEmail, string? City, int Version);
}
