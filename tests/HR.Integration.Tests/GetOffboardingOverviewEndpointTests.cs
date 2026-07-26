using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetOffboardingOverviewEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("ff0000ff-0000-0000-0000-000000000001");

    public GetOffboardingOverviewEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_OffboardingOverview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/offboarding-overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OffboardingOverview_Returns_HasPlan_False_When_Employee_Has_No_Plan()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.False(payload.HasPlan);
        Assert.Null(payload.PlanStatus);
        Assert.Null(payload.LastWorkingDay);
        Assert.Null(payload.Notes);
        Assert.Empty(payload.Tasks);
    }

    [Fact]
    public async Task Get_OffboardingOverview_Returns_Plan_And_Tasks_With_Mixed_AssignTo_And_Status_After_Start()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var categoryId = await CreateAssetCategoryAsync(client, companyId, "IT Equipment");
        var assetId = await CreateAssetAsync(client, companyId, categoryId, $"OB-{Guid.NewGuid():N}");
        await AssignAssetAsync(client, companyId, assetId, employeeId);

        var lastWorkingDay = new DateOnly(2026, 8, 1);
        await StartOffboardingAsync(client, companyId, employeeId, lastWorkingDay, "Resigned.");

        // Complete the employee's asset-return task so we exercise a Completed status
        // alongside the still-Pending HR / manager checklist tasks.
        await CompleteEmployeeTaskAsync(client, companyId, employeeId, "Return asset:");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.True(payload.HasPlan);
        Assert.Equal("InProgress", payload.PlanStatus);
        Assert.Equal(lastWorkingDay, payload.LastWorkingDay);
        Assert.Equal("Resigned.", payload.Notes);

        // 1 asset-return (Employee) task + 1 HR document-review task + 4 manager checklist tasks.
        Assert.Equal(6, payload.Tasks.Count);

        // Matched by AssignTo rather than by the generic Tasks-module id returned from
        // CompleteEmployeeTaskAsync — that id belongs to the Tasks module's own TaskItem, a
        // different entity from the OffboardingTask this endpoint returns (linked, not identical).
        var completedAssetTask = Assert.Single(payload.Tasks, t => t.AssignTo == "Employee");
        Assert.Equal("Completed", completedAssetTask.Status);
        Assert.NotNull(completedAssetTask.CompletedAt);
        Assert.Equal(completedAssetTask.CompletedAt, completedAssetTask.UpdatedAt);

        var hrTask = Assert.Single(payload.Tasks, t => t.AssignTo == "HR");
        Assert.Equal("Pending", hrTask.Status);
        Assert.Null(hrTask.CompletedAt);
        Assert.Equal(lastWorkingDay, hrTask.DueDate);

        var managerTasks = payload.Tasks.Where(t => t.AssignTo == "Manager").ToList();
        Assert.Equal(4, managerTasks.Count);
        Assert.All(managerTasks, t =>
        {
            Assert.Equal("Pending", t.Status);
            Assert.Null(t.CompletedAt);
            Assert.Equal(lastWorkingDay, t.DueDate);
        });
    }

    [Fact]
    public async Task Get_OffboardingOverview_Does_Not_Return_Plan_From_A_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = AdminClient(companyA);
        var employeeId = await CreateEmployeeAsync(clientA, companyA);
        await StartOffboardingAsync(clientA, companyA, employeeId, new DateOnly(2026, 8, 1), "Resigned.");

        // Query the same employeeId, but scoped to a different company/tenant.
        using var clientB = AdminClient(companyB);
        var response = await clientB.GetAsync(
            $"/api/companies/{companyB}/employees/{employeeId}/offboarding-overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasPlan);
        Assert.Null(payload.PlanStatus);
        Assert.Null(payload.LastWorkingDay);
        Assert.Null(payload.Notes);
        Assert.Empty(payload.Tasks);
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
            workEmail = $"jamie.smith.{Guid.NewGuid():N}@offboardoverviewtest.example",
            startDate = "2026-01-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"OFB-{Guid.NewGuid():N}",
            employmentTypeId,
            departmentId,
            locationId,
            positionProfileId
        });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
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

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
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

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task<Guid> CreateAssetCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories",
            new { companyId, name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<Guid> CreateAssetAsync(HttpClient client, Guid companyId, Guid categoryId, string assetNumber)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets",
            new { companyId, assetNumber, categoryId, name = $"Asset {assetNumber}" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task AssignAssetAsync(HttpClient client, Guid companyId, Guid assetId, Guid employeeId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments",
            new
            {
                companyId,
                assetId,
                employeeId,
                assignedBy = AdminUserId,
                notes = "Integration test assignment",
            });
        resp.EnsureSuccessStatusCode();
    }

    private async Task StartOffboardingAsync(
        HttpClient client, Guid companyId, Guid employeeId, DateOnly lastWorkingDay, string? notes)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = lastWorkingDay.ToString("yyyy-MM-dd"), notes });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CompleteEmployeeTaskAsync(
        HttpClient client, Guid companyId, Guid employeeId, string titleContains)
    {
        var listResp = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks");
        listResp.EnsureSuccessStatusCode();
        var payload = await listResp.Content.ReadFromJsonAsync<EmployeeTasksPayload>();
        var task = payload!.Items.Single(t => t.Title.Contains(titleContains));

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        completeResp.EnsureSuccessStatusCode();

        return task.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record EmployeeTasksPayload(IReadOnlyList<EmployeeTaskItem> Items);

    private sealed record EmployeeTaskItem(Guid Id, string Title);

    private sealed record OverviewPayload(
        Guid EmployeeId,
        bool HasPlan,
        string? PlanStatus,
        DateOnly? LastWorkingDay,
        string? Notes,
        List<OffboardingTaskOverviewItemPayload> Tasks);

    private sealed record OffboardingTaskOverviewItemPayload(
        Guid Id,
        string Title,
        string? Description,
        string AssignTo,
        string Status,
        DateOnly? DueDate,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
