using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEmployeePromotionHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser1 = new("ee000002-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser2 = new("ee000002-0000-0000-0000-000000000002");
    private static readonly Guid AdminUser3 = new("ee000002-0000-0000-0000-000000000003");

    public GetEmployeePromotionHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser3, SystemRoles.HrAdministrator);
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

    [Fact]
    public async Task Get_PromotionHistory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/promotions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PromotionHistory_Returns_NotFound_For_Unknown_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser1, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/promotions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_PromotionHistory_Returns_Ok_With_Empty_List_When_No_Promotions()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser2, companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId, _) =
            await CreateEmployeeReferenceDataAsync(client, companyId);
        var employee = await CreateEmployeeAsync(client, companyId, departmentId, locationId, positionProfileId, employmentTypeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employee}/promotions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Empty(items.EnumerateArray());
    }

    [Fact]
    public async Task Get_PromotionHistory_Returns_Promotions_Ordered_By_EffectiveDate_Descending()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(AdminUser3, companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId, defaultLeavePolicyId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);
        var employee = await CreateEmployeeAsync(client, companyId, departmentId, locationId, positionProfileId, employmentTypeId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var firstPromotionPositionId = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, defaultLeavePolicyId);
        var firstPromotionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee}/promotions",
            new
            {
                companyId,
                employeeId = employee,
                newPositionProfileId = firstPromotionPositionId,
                effectiveDate = today.AddDays(-30).ToString("yyyy-MM-dd"),
                reason = "Earlier promotion",
                confirmBackdatedEffectiveDate = true,
            });
        firstPromotionResponse.EnsureSuccessStatusCode();

        var secondPromotionPositionId = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, defaultLeavePolicyId);
        var secondPromotionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee}/promotions",
            new
            {
                companyId,
                employeeId = employee,
                newPositionProfileId = secondPromotionPositionId,
                effectiveDate = today.ToString("yyyy-MM-dd"),
                reason = "Latest promotion",
            });
        secondPromotionResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employee}/promotions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        // Most recent EffectiveDate first
        Assert.Equal("Latest promotion", items[0].GetProperty("reason").GetString());
        Assert.Equal("Earlier promotion", items[1].GetProperty("reason").GetString());
    }

    private static async Task<Guid> CreatePositionProfileAsync(
        HttpClient client, Guid companyId, Guid departmentId, Guid locationId, Guid defaultLeavePolicyId)
    {
        var ppResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        ppResp.EnsureSuccessStatusCode();
        return (await ppResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task<Guid> CreateEmployeeAsync(
        HttpClient client, Guid companyId, Guid departmentId, Guid locationId, Guid positionProfileId, Guid employmentTypeId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Bob",
            lastName = "Jones",
            workEmail = $"bob.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"PROMOHIST-{Guid.NewGuid():N}",
            employmentTypeId,
            departmentId,
            locationId,
            positionProfileId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId, Guid DefaultLeavePolicyId)>
        CreateEmployeeReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"LeavePolicy-{Guid.NewGuid():N}", carryOverDays = 5, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var ppResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        ppResp.EnsureSuccessStatusCode();
        var positionProfileId = (await ppResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId, defaultLeavePolicyId);
    }

    private sealed record IdPayload(Guid Id);
}
