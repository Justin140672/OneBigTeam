using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetOffboardingStatusEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("ff0000ff-0000-0000-0000-000000000002");

    public GetOffboardingStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_OffboardingStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/offboarding-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OffboardingStatus_Returns_HasPlan_False_When_Employee_Has_No_Plan()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/offboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasPlan);
        Assert.Null(payload.Status);
    }

    [Fact]
    public async Task Get_OffboardingStatus_Returns_InProgress_After_Started()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartOffboardingAsync(client, companyId, employeeId, new DateOnly(2026, 8, 1), "Resigned.");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasPlan);
        Assert.Equal("InProgress", payload.Status);
    }

    [Fact]
    public async Task Get_OffboardingStatus_Returns_Completed_After_All_Tasks_Completed()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartOffboardingAsync(client, companyId, employeeId, new DateOnly(2026, 8, 1), "Resigned.");

        // No manager and no assets were set, so StartOffboardingHandler generates exactly 5
        // tasks — 1 HR document-review task (always unassigned) + 4 manager exit-checklist
        // tasks (fall back to unassigned since there's no manager to assign them to) — none of
        // which are assigned to the employee themselves, so they must be fetched via the
        // unassigned-tasks inbox rather than GET /employees/{employeeId}/tasks.
        var listResp = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        listResp.EnsureSuccessStatusCode();
        var tasks = (await listResp.Content.ReadFromJsonAsync<UnassignedTasksPayload>())!.Items
            .Where(t => t.Source == "Offboarding")
            .ToList();

        Assert.Equal(5, tasks.Count);

        foreach (var task in tasks)
        {
            var completeResp = await client.PostAsync(
                $"/api/companies/{companyId}/tasks/{task.Id}/complete",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            completeResp.EnsureSuccessStatusCode();
        }

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasPlan);
        Assert.Equal("Completed", payload.Status);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var (departmentId, locationId, positionProfileId, employmentTypeId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);

        var resp = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Jamie",
            lastName = "Smith",
            workEmail = $"jamie.smith.{Guid.NewGuid():N}@offboardstatustest.example",
            startDate = "2026-01-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"OFB-{Guid.NewGuid():N}",
            employmentTypeId,
            departmentId,
            locationId,
            positionProfileId,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)>
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

        var ppResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}" });
        ppResp.EnsureSuccessStatusCode();
        var positionProfileId = (await ppResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task StartOffboardingAsync(
        HttpClient client, Guid companyId, Guid employeeId, DateOnly lastWorkingDay, string? notes)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = lastWorkingDay.ToString("yyyy-MM-dd"), notes });
        resp.EnsureSuccessStatusCode();
    }

    private sealed record IdPayload(Guid Id);

    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskPayload> Items);

    private sealed record UnassignedTaskPayload(Guid Id, string Title, string? Source);

    private sealed record StatusPayload(bool HasPlan, string? Status);
}
