using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class EmergencyContactsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid EcUser1 = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid EcUser2 = new("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid EcUser3 = new("eeeeeeee-0000-0000-0000-000000000003");
    private static readonly Guid EcUser4 = new("eeeeeeee-0000-0000-0000-000000000004");

    public EmergencyContactsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        // Seed HrAdministrator role so tests can call employee:manage endpoints.
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, EcUser1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EcUser1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EcUser2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EcUser2, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EcUser3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EcUser3, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EcUser4, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EcUser4, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId)> CreateEmployeeAsync(Guid adminUserId)
    {
        var companyId = Guid.NewGuid();

        // Use the admin user to create the employee record
        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, adminUserId.ToString());
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, adminUserId, companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId) =
            await CreateEmployeeReferenceDataAsync(adminClient, companyId);

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
            employeeNumber = $"EC-{Guid.NewGuid():N}",
            employmentTypeId,
            departmentId,
            locationId,
            positionProfileId
        });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<EmployeeIdPayload>();

        await TestRoleSeeder.AssignRoleAsync(_factory, created!.Id, SystemRoles.Employee);

        // Return a client that acts as the created employee (sub = employee ID)
        // so /me/ endpoints can find the record.
        var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, created!.Id.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        return (employeeClient, companyId, created.Id);
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)>
        CreateEmployeeReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<EmployeeIdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<EmployeeIdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<EmployeeIdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<EmployeeIdPayload>())!.Id;

        var ppResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        ppResp.EnsureSuccessStatusCode();
        var positionProfileId = (await ppResp.Content.ReadFromJsonAsync<EmployeeIdPayload>())!.Id;

        var etResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<EmployeeIdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    // ── Authorization ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_My_Emergency_Contacts_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/me/emergency-contacts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_My_Emergency_Contact_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/me/emergency-contacts",
            new { name = "Jane Doe", relationship = "Spouse", phoneNumber = "07700 900000" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Emergency_Contacts_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/emergency-contacts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Initial state ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_My_Emergency_Contacts_Returns_Empty_List_Initially()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(EcUser1);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ContactsPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Contacts);
    }

    // ── Add ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_My_Emergency_Contact_Creates_And_Returns_Contact()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(EcUser2);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts",
            new
            {
                name         = "Jane Doe",
                relationship = "Spouse",
                phoneNumber  = "07700 900100",
                email        = "jane.doe@example.com"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ContactPayload>();
        Assert.NotNull(created);
        Assert.Equal("Jane Doe", created!.Name);
        Assert.Equal("Spouse", created.Relationship);
        Assert.Equal("07700 900100", created.PhoneNumber);
        Assert.Equal("jane.doe@example.com", created.Email);
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task Post_My_Emergency_Contact_Returns_422_When_Name_Missing()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(EcUser3);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts",
            new { relationship = "Spouse", phoneNumber = "07700 900000" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_My_Emergency_Contact_Returns_422_When_Relationship_Missing()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(EcUser3);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts",
            new { name = "Jane Doe", phoneNumber = "07700 900000" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_My_Emergency_Contact_Returns_422_When_Phone_Missing()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(EcUser3);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts",
            new { name = "Jane Doe", relationship = "Spouse" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_My_Emergency_Contact_Returns_422_When_Email_Invalid()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(EcUser3);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts",
            new { name = "Jane Doe", relationship = "Spouse", phoneNumber = "07700 900000", email = "not-an-email" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── Full round-trip ────────────────────────────────────────────────────────

    [Fact]
    public async Task Full_Roundtrip_Add_Update_Delete_And_Get_Employee_Contacts()
    {
        var (client, companyId, employeeId) = await CreateEmployeeAsync(EcUser4);

        // Add contact
        var addResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts",
            new { name = "Bob Smith", relationship = "Parent", phoneNumber = "01234 567890" });
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var added = await addResponse.Content.ReadFromJsonAsync<ContactPayload>();
        Assert.NotNull(added);

        // GET /me should list it
        var listResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await listResponse.Content.ReadFromJsonAsync<ContactsPayload>();
        Assert.Single(list!.Contacts);
        Assert.Equal("Bob Smith", list.Contacts[0].Name);

        // Update it
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts/{added!.Id}",
            new { name = "Robert Smith", relationship = "Father", phoneNumber = "01234 999999", email = "robert@example.com" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // GET /me should reflect update
        var listAfterUpdate = await client.GetAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts");
        var updatedList = await listAfterUpdate.Content.ReadFromJsonAsync<ContactsPayload>();
        Assert.Single(updatedList!.Contacts);
        Assert.Equal("Robert Smith", updatedList.Contacts[0].Name);
        Assert.Equal("Father", updatedList.Contacts[0].Relationship);
        Assert.Equal("robert@example.com", updatedList.Contacts[0].Email);

        // HR admin GET should also see it — the non-"/me" route is gated by employee:read, which a
        // plain employee (which `client` acts as) does not hold, so use an HR-admin client.
        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EcUser4.ToString());
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EcUser4, SystemRoles.HrAdministrator, companyId);

        var adminResponse = await adminClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/emergency-contacts");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

        var adminList = await adminResponse.Content.ReadFromJsonAsync<ContactsPayload>();
        Assert.Single(adminList!.Contacts);

        // Delete it
        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts/{added.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // GET /me should be empty again
        var listAfterDelete = await client.GetAsync(
            $"/api/companies/{companyId}/employees/me/emergency-contacts");
        var emptyList = await listAfterDelete.Content.ReadFromJsonAsync<ContactsPayload>();
        Assert.Empty(emptyList!.Contacts);
    }

    // ── Isolation ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Returns_404_For_Contact_Belonging_To_Different_Employee()
    {
        var (client1, companyId1, _) = await CreateEmployeeAsync(EcUser1);

        // User1 adds a contact
        var addResponse = await client1.PostAsJsonAsync(
            $"/api/companies/{companyId1}/employees/me/emergency-contacts",
            new { name = "Test Contact", relationship = "Friend", phoneNumber = "07700 000000" });
        var added = await addResponse.Content.ReadFromJsonAsync<ContactPayload>();

        // Different company/user — should 404
        var client2 = _factory.CreateClient();
        client2.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EcUser2.ToString());
        client2.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId1.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EcUser2, SystemRoles.HrAdministrator, companyId1);

        var deleteResponse = await client2.DeleteAsync(
            $"/api/companies/{companyId1}/employees/me/emergency-contacts/{added!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    private sealed record EmployeeIdPayload(Guid Id);
    private sealed record ContactPayload(Guid Id, string Name, string Relationship, string PhoneNumber, string? Email);
    private sealed record ContactsPayload(List<ContactPayload> Contacts);
}
