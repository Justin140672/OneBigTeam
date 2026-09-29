using HR.Modules.Employees.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEmployeeTimelineEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser1 = new("ee000003-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser2 = new("ee000003-0000-0000-0000-000000000002");
    private static readonly Guid AdminUser3 = new("ee000003-0000-0000-0000-000000000003");
    private static readonly Guid AdminUser4 = new("ee000003-0000-0000-0000-000000000004");
    private static readonly Guid AdminUser5 = new("ee000003-0000-0000-0000-000000000005");

    public GetEmployeeTimelineEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var admin in new[] { AdminUser1, AdminUser2, AdminUser3, AdminUser4, AdminUser5 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, admin, SystemRoles.Employee);
                await TestRoleSeeder.AssignRoleAsync(factory, admin, SystemRoles.HrAdministrator);
            }
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    [Fact]
    public async Task Get_Timeline_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/timeline");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Timeline_Returns_NotFound_For_Unknown_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser1, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/timeline");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Timeline_Returns_Forbidden_When_Route_CompanyId_Does_Not_Match_Caller_Tenant()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser2, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{otherCompanyId}/employees/{employee}/timeline");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Timeline_Returns_Ok_With_EmployeeJoined_Entry_Only_Right_After_Creating_An_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser3, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employee}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        var item = Assert.Single(items.EnumerateArray());
        Assert.Equal("EmployeeJoined", item.GetProperty("eventType").GetString());
        Assert.Equal(1, payload.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Get_Timeline_Returns_Ok_For_Employee_Viewing_Own_Timeline()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await AdminClient(AdminUser4, companyId);

        var employee = await CreateEmployeeAsync(adminClient, companyId);

        await TestRoleSeeder.AssignRoleAsync(_factory, employee, SystemRoles.Employee);
        using var selfClient = await ClientFor(employee, companyId);

        var response = await selfClient.GetAsync($"/api/companies/{companyId}/employees/{employee}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task Get_Timeline_Returns_Ok_With_Empty_List_For_Unrelated_Employee()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await AdminClient(AdminUser5, companyId);

        var employee = await CreateEmployeeAsync(adminClient, companyId);
        var unrelatedEmployee = await CreateEmployeeAsync(adminClient, companyId);

        await TestRoleSeeder.AssignRoleAsync(_factory, unrelatedEmployee, SystemRoles.Employee);
        using var unrelatedClient = await ClientFor(unrelatedEmployee, companyId);

        var response = await unrelatedClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employee}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items");
        Assert.Empty(items.EnumerateArray());
    }

    [Fact]
    public async Task Get_Timeline_Returns_BadRequest_For_Invalid_PageSize()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser2, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employee}/timeline?pageSize=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_Timeline_Returns_BadRequest_For_Invalid_PageNumber()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser3, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employee}/timeline?pageNumber=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_Timeline_Returns_EmployeeJoined_Entry_After_Creating_An_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser4, companyId);

        var employee = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employee}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("eventType").GetString() == "EmployeeJoined");
        Assert.True(payload.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Get_Timeline_Shows_Promotion_Entry_Visible_To_HrAdministrator_After_Promoting_An_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser5, companyId);

        var referenceData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId,
                referenceData,
                firstName: "Priya",
                lastName: "Promotable",
                workEmail: $"priya.{Guid.NewGuid():N}@example.com",
                employeeNumber: $"PRM-{Guid.NewGuid():N}"));
        createResponse.EnsureSuccessStatusCode();
        var employee = (await createResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"LeavePolicy-{Guid.NewGuid():N}",
            carryOverDays = 0,
            allowNegativeBalance = false
        });
        leavePolicyResponse.EnsureSuccessStatusCode();
        var leavePolicyId = (await leavePolicyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var positionProfileResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId = referenceData.DepartmentId,
            locationId = referenceData.LocationId,
            title = $"Senior Role {Guid.NewGuid():N}",
            defaultLeavePolicyId = leavePolicyId,
        });
        positionProfileResponse.EnsureSuccessStatusCode();
        var newPositionId = (await positionProfileResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var promoteResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee}/promotions",
            new
            {
                companyId,
                employeeId = employee,
                newPositionProfileId = newPositionId,
                effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                reason = "Promotion",
            });
        promoteResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employee}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("eventType").GetString() == "EmployeePromoted");
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var referenceData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId,
                referenceData,
                firstName: "Tina",
                lastName: "Timeline",
                workEmail: $"tina.{Guid.NewGuid():N}@example.com",
                employeeNumber: $"TML-{Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private sealed record IdPayload(Guid Id);
}
