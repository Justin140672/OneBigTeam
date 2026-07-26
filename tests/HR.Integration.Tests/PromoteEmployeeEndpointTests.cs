using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class PromoteEmployeeEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser1 = new("ee000001-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser2 = new("ee000001-0000-0000-0000-000000000002");
    private static readonly Guid AdminUser3 = new("ee000001-0000-0000-0000-000000000003");
    private static readonly Guid AdminUser4 = new("ee000001-0000-0000-0000-000000000004");
    private static readonly Guid AdminUser5 = new("ee000001-0000-0000-0000-000000000005");

    public PromoteEmployeeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser1, SystemRoles.HrAdministrator);
            // GetEmployee (used below to verify the position change took effect) is gated by
            // "role:employee", which is a strict role check, not implied by HrAdministrator alone.
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser4, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser5, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Post_Promotions_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/promotions",
            new
            {
                newPositionProfileId = Guid.NewGuid(),
                effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                reason = "Promotion",
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Promotions_Creates_Promotion_And_Applies_Immediately_When_Effective_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(AdminUser1, companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId, defaultLeavePolicyId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);
        var newPositionProfileId = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, defaultLeavePolicyId);
        var employee = await CreateEmployeeAsync(
            client, companyId, departmentId, locationId, positionProfileId, employmentTypeId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee}/promotions",
            new
            {
                companyId,
                employeeId = employee,
                newPositionProfileId,
                effectiveDate = today.ToString("yyyy-MM-dd"),
                reason = "Strong performance",
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, payload.GetProperty("id").GetGuid());
        Assert.Equal(employee, payload.GetProperty("employeeId").GetGuid());
        Assert.Equal(newPositionProfileId, payload.GetProperty("newPositionProfileId").GetGuid());
        Assert.Equal(positionProfileId, payload.GetProperty("previousPositionProfileId").GetGuid());
        Assert.Equal("Strong performance", payload.GetProperty("reason").GetString());
        Assert.NotEqual(JsonValueKind.Null, payload.GetProperty("completedAt").ValueKind);

        // The employee's position should be updated immediately since EffectiveDate <= today.
        var getResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employee}");
        getResponse.EnsureSuccessStatusCode();
        var employeePayload = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(newPositionProfileId, employeePayload.GetProperty("positionProfileId").GetGuid());
    }

    [Fact]
    public async Task Post_Promotions_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(AdminUser2, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/promotions",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                newPositionProfileId = Guid.NewGuid(),
                effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                reason = "Promotion",
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Promotions_Returns_Conflict_When_Backdated_And_Not_Confirmed()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(AdminUser3, companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId, defaultLeavePolicyId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);
        var newPositionProfileId = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, defaultLeavePolicyId);
        var employee = await CreateEmployeeAsync(client, companyId, departmentId, locationId, positionProfileId, employmentTypeId);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee}/promotions",
            new
            {
                companyId,
                employeeId = employee,
                newPositionProfileId,
                effectiveDate = yesterday.ToString("yyyy-MM-dd"),
                reason = "Backdated promotion",
                confirmBackdatedEffectiveDate = false,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_Promotions_Applies_Backdated_Promotion_When_Confirmed()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(AdminUser4, companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId, defaultLeavePolicyId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);
        var newPositionProfileId = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, defaultLeavePolicyId);
        var employee = await CreateEmployeeAsync(client, companyId, departmentId, locationId, positionProfileId, employmentTypeId);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee}/promotions",
            new
            {
                companyId,
                employeeId = employee,
                newPositionProfileId,
                effectiveDate = yesterday.ToString("yyyy-MM-dd"),
                reason = "Backdated promotion, confirmed",
                confirmBackdatedEffectiveDate = true,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_Promotions_Returns_UnprocessableEntity_When_Reason_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(AdminUser5, companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId, defaultLeavePolicyId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);
        var newPositionProfileId = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, defaultLeavePolicyId);
        var employee = await CreateEmployeeAsync(client, companyId, departmentId, locationId, positionProfileId, employmentTypeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee}/promotions",
            new
            {
                companyId,
                employeeId = employee,
                newPositionProfileId,
                effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                reason = string.Empty,
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Promotions_Returns_UnprocessableEntity_When_NewPositionProfileId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(AdminUser5, companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId, _) =
            await CreateEmployeeReferenceDataAsync(client, companyId);
        var employee = await CreateEmployeeAsync(client, companyId, departmentId, locationId, positionProfileId, employmentTypeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee}/promotions",
            new
            {
                companyId,
                employeeId = employee,
                newPositionProfileId = Guid.Empty,
                effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                reason = "Promotion",
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
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
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"PROMO-{Guid.NewGuid():N}",
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
