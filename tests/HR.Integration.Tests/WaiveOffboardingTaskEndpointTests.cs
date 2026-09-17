using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Spec SPEC-OFF-01: PUT /api/companies/{companyId}/offboarding/tasks/{offboardingTaskId}/waive.
[Collection("Integration")]
public class WaiveOffboardingTaskEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("dd000004-0000-0000-0000-000000000001");

    public WaiveOffboardingTaskEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
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

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await CreateReferenceDataAsync(client, companyId);

        var resp = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Jamie",
            lastName = "Smith",
            workEmail = $"jamie.smith.{Guid.NewGuid():N}@waivetasktest.example",
            startDate = "2026-01-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"WAV-{Guid.NewGuid():N}",
            employmentTypeId = refData.EmploymentTypeId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId
        });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private async Task<Guid> StartOffboardingAndGetFirstTaskIdAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        // Future last-working-day so tasks aren't auto-waived by backdated-departure reconciliation.
        var lastWorkingDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay, notes = "Resigned." });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OffboardingPlanPayload>();
        return payload!.GeneratedTaskIds[0];
    }

    [Fact]
    public async Task Put_WaiveOffboardingTask_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/offboarding/tasks/{taskId}/waive",
            new { companyId, offboardingTaskId = taskId, reason = "Not required." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_WaiveOffboardingTask_Waives_A_Pending_Task()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var taskId = await StartOffboardingAndGetFirstTaskIdAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/offboarding/tasks/{taskId}/waive",
            new { companyId, offboardingTaskId = taskId, reason = "Not required for this departure." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WaiveResponsePayload>();
        Assert.NotNull(payload);
        Assert.Equal(taskId, payload!.Id);
        Assert.Equal("Waived", payload.Status);
        Assert.NotNull(payload.SkippedAt);
        Assert.Equal(AdminUserId, payload.SkippedByUserId);

        var overviewResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");
        overviewResponse.EnsureSuccessStatusCode();
        var overview = await overviewResponse.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(overview);
        Assert.Contains(overview!.Tasks, t => t.Id == taskId && t.Status == "Waived");
    }

    [Fact]
    public async Task Put_WaiveOffboardingTask_Returns_NotFound_For_NonExistent_Task()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var taskId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/offboarding/tasks/{taskId}/waive",
            new { companyId, offboardingTaskId = taskId, reason = "Not required." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_WaiveOffboardingTask_Returns_Conflict_When_Task_Already_Completed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var taskId = await StartOffboardingAndGetFirstTaskIdAsync(client, companyId, employeeId);

        var firstWaive = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/offboarding/tasks/{taskId}/waive",
            new { companyId, offboardingTaskId = taskId, reason = "Not required." });
        firstWaive.EnsureSuccessStatusCode();

        var secondWaive = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/offboarding/tasks/{taskId}/waive",
            new { companyId, offboardingTaskId = taskId, reason = "Still not required." });

        Assert.Equal(HttpStatusCode.Conflict, secondWaive.StatusCode);
    }

    [Fact]
    public async Task Put_WaiveOffboardingTask_Returns_ValidationError_When_Reason_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var taskId = await StartOffboardingAndGetFirstTaskIdAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/offboarding/tasks/{taskId}/waive",
            new { companyId, offboardingTaskId = taskId, reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OffboardingPlanPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        DateOnly LastWorkingDay,
        string Status,
        string? Notes,
        IReadOnlyList<Guid> GeneratedTaskIds,
        DateTimeOffset CreatedAt);

    private sealed record WaiveResponsePayload(Guid Id, string Status, DateTimeOffset? SkippedAt, Guid? SkippedByUserId);

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
