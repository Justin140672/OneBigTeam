using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateMyContactDetailsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid ContactUser1 = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ContactUser2 = new("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid ContactUser3 = new("cccccccc-0000-0000-0000-000000000003");

    public UpdateMyContactDetailsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ContactUser1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ContactUser1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ContactUser2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ContactUser2, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ContactUser3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ContactUser3, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)> CreateReferenceDataAsync(
        HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept {Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType {Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc {Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Title {Guid.NewGuid():N}", defaultLeavePolicyId });
        posResp.EnsureSuccessStatusCode();
        var positionProfileId = (await posResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var empTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType {Guid.NewGuid():N}" });
        empTypeResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await empTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId)> CreateEmployeeAsync(Guid adminUserId)
    {
        var companyId = Guid.NewGuid();

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, adminUserId.ToString());
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, adminUserId, companyId);

        var refData = await CreateReferenceDataAsync(adminClient, companyId);

        var createResponse = await adminClient.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Test",
            lastName = "Employee",
            workEmail = $"test.{Guid.NewGuid():N}@example.com",
            startDate = "2026-01-01",
            dateOfBirth = "1990-06-15",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = refData.EmploymentTypeId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId
        });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<EmployeeIdPayload>();

        await TestRoleSeeder.AssignRoleAsync(_factory, created!.Id, SystemRoles.Employee);

        var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, created!.Id.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        return (employeeClient, companyId, created.Id);
    }

    [Fact]
    public async Task Get_Contact_Details_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/me/contact-details");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/me/contact-details",
            new { addressLine1 = "1 Test St", city = "London", postCode = "SW1A 1AA", country = "UK" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Contact_Details_Returns_Work_Email_And_Nulls_Initially()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(ContactUser1);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/me/contact-details");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ContactDetailsPayload>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.WorkEmail));
        Assert.Null(payload.PersonalEmail);
        Assert.Null(payload.PhoneNumber);
        Assert.Null(payload.AddressLine1);
    }

    [Fact]
    public async Task Put_Contact_Details_Persists_And_Returns_Updated_Values()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(ContactUser2);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/contact-details",
            new
            {
                personalEmail = "personal@example.com",
                phoneNumber   = "07700 900001",
                homePhone     = "01234 567890",
                addressLine1  = "42 Acacia Avenue",
                addressLine2  = "Flat 3",
                city          = "Manchester",
                county        = "Greater Manchester",
                postCode      = "M1 1AA",
                country       = "United Kingdom"
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var getResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/me/contact-details");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var payload = await getResponse.Content.ReadFromJsonAsync<ContactDetailsPayload>();
        Assert.NotNull(payload);
        Assert.Equal("personal@example.com", payload!.PersonalEmail);
        Assert.Equal("07700 900001", payload.PhoneNumber);
        Assert.Equal("01234 567890", payload.HomePhone);
        Assert.Equal("42 Acacia Avenue", payload.AddressLine1);
        Assert.Equal("Flat 3", payload.AddressLine2);
        Assert.Equal("Manchester", payload.City);
        Assert.Equal("Greater Manchester", payload.County);
        Assert.Equal("M1 1AA", payload.PostCode);
        Assert.Equal("United Kingdom", payload.Country);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_422_When_Address_Line1_Missing()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(ContactUser3);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/contact-details",
            new
            {
                city     = "London",
                postCode = "SW1A 1AA",
                country  = "United Kingdom"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_422_When_City_Missing()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(ContactUser3);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/contact-details",
            new
            {
                addressLine1 = "1 Test Street",
                postCode     = "SW1A 1AA",
                country      = "United Kingdom"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_422_When_PostCode_Missing()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(ContactUser3);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/contact-details",
            new
            {
                addressLine1 = "1 Test Street",
                city         = "London",
                country      = "United Kingdom"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_422_When_Country_Missing()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(ContactUser3);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/contact-details",
            new
            {
                addressLine1 = "1 Test Street",
                city         = "London",
                postCode     = "SW1A 1AA"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Contact_Details_Returns_422_When_Personal_Email_Invalid()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(ContactUser3);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/contact-details",
            new
            {
                personalEmail = "not-an-email",
                addressLine1  = "1 Test Street",
                city          = "London",
                postCode      = "SW1A 1AA",
                country       = "United Kingdom"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record EmployeeIdPayload(Guid Id);
    private sealed record IdPayload(Guid Id);

    private sealed record ContactDetailsPayload(
        string WorkEmail,
        string? PersonalEmail,
        string? PhoneNumber,
        string? HomePhone,
        string? AddressLine1,
        string? AddressLine2,
        string? City,
        string? County,
        string? PostCode,
        string? Country);
}
