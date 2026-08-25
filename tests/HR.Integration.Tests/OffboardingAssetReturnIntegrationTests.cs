using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// OFF-04: integrates the Assets lifecycle with offboarding — completing an offboarding asset-return
// checklist item (via the Tasks module's generic /complete endpoint) must actually return the real
// Assets-module assignment, an outstanding/unresolved asset-return task must keep blocking plan
// completion, and cancelling the offboarding plan (via leaving-process cancellation) must never touch
// the underlying asset assignment.
[Collection("Integration")]
public class OffboardingAssetReturnIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ff040001-0000-0000-0000-000000000001");

    public OffboardingAssetReturnIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Off04", "Employee", $"off04.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task<(Guid assetId, Guid assignmentId)> AssignAssetAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var categoryResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "Electronics" });
        categoryResp.EnsureSuccessStatusCode();
        var category = await categoryResp.Content.ReadFromJsonAsync<IdPayload>();

        var assetResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets",
            new { companyId, assetNumber = $"OFF04-{Guid.NewGuid():N}", categoryId = category!.Id, name = "Laptop" });
        assetResp.EnsureSuccessStatusCode();
        var asset = await assetResp.Content.ReadFromJsonAsync<IdPayload>();

        var assignResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset!.Id}/assignments",
            new { companyId, assetId = asset.Id, employeeId, assignedBy = Guid.NewGuid() });
        assignResp.EnsureSuccessStatusCode();
        var assignment = await assignResp.Content.ReadFromJsonAsync<IdPayload>();

        return (asset.Id, assignment!.Id);
    }

    // Relative to "today" so this test never becomes "backdated" as time passes.
    private static readonly DateOnly LeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private static readonly DateOnly LastWorkingDay = LeavingDate.AddDays(-1);

    private static async Task StartLeavingProcessAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        response.EnsureSuccessStatusCode();
    }

    // Manager-checklist and document-review tasks are created unassigned (no manager set up in
    // these tests) and surface via /tasks/unassigned, but the asset-return task is created with
    // AssignedEmployeeId == the employee being offboarded (see StartOffboardingHandler.
    // CreateAssetReturnTasksAsync) and therefore only shows up via the employee's own task list.
    private static async Task<Guid> FindTaskItemIdBySourceEntityAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid sourceEntityId)
    {
        var unassignedResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        unassignedResponse.EnsureSuccessStatusCode();
        var unassignedPayload = await unassignedResponse.Content.ReadFromJsonAsync<UnassignedTasksPayload>();
        var unassignedMatch = unassignedPayload!.Items.SingleOrDefault(t => t.SourceEntityId == sourceEntityId);
        if (unassignedMatch is not null)
            return unassignedMatch.Id;

        var employeeResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks");
        employeeResponse.EnsureSuccessStatusCode();
        var employeePayload = await employeeResponse.Content.ReadFromJsonAsync<EmployeeTasksPayload>();
        var employeeMatch = Assert.Single(employeePayload!.Items, t => t.SourceEntityId == sourceEntityId);
        return employeeMatch.Id;
    }

    private static async Task<OffboardingOverviewPayload> GetOverviewAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OffboardingOverviewPayload>())!;
    }

    [Fact]
    public async Task CompletingAssetReturnTask_Returns_The_Real_Assignment_And_Marks_Asset_Available()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var (assetId, _) = await AssignAssetAsync(client, companyId, employeeId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        var assetReturnTask = Assert.Single(overview.Tasks, t => t.Title.Contains("Laptop"));
        Assert.Equal("Pending", assetReturnTask.Status);

        var taskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, assetReturnTask.Id);

        var completeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/tasks/{taskItemId}/complete",
            new { companyId, id = taskItemId });
        completeResp.EnsureSuccessStatusCode();

        var assetAfter = await client.GetFromJsonAsync<AssetPayload>($"/api/companies/{companyId}/assets/{assetId}");
        Assert.Equal("Available", assetAfter!.Status);

        var overviewAfter = await GetOverviewAsync(client, companyId, employeeId);
        var assetReturnTaskAfter = Assert.Single(overviewAfter.Tasks, t => t.Id == assetReturnTask.Id);
        Assert.Equal("Completed", assetReturnTaskAfter.Status);
    }

    [Fact]
    public async Task CompletingAssetReturnTask_With_Lost_Outcome_Leaves_Asset_UnderRepair_Not_Available()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var (assetId, _) = await AssignAssetAsync(client, companyId, employeeId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        var assetReturnTask = Assert.Single(overview.Tasks, t => t.Title.Contains("Laptop"));
        var taskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, assetReturnTask.Id);

        var completeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/tasks/{taskItemId}/complete",
            new { companyId, id = taskItemId, outcomeDecision = "Lost", outcomeReason = "Never returned it." });
        completeResp.EnsureSuccessStatusCode();

        var assetAfter = await client.GetFromJsonAsync<AssetPayload>($"/api/companies/{companyId}/assets/{assetId}");
        Assert.Equal("UnderRepair", assetAfter!.Status);
        Assert.NotEqual("Available", assetAfter.Status);

        var overviewAfter = await GetOverviewAsync(client, companyId, employeeId);
        var assetReturnTaskAfter = Assert.Single(overviewAfter.Tasks, t => t.Id == assetReturnTask.Id);
        Assert.Equal("Completed", assetReturnTaskAfter.Status);
    }

    [Fact]
    public async Task OutstandingAssetReturnTask_Blocks_Plan_Completion_Until_Resolved()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        await AssignAssetAsync(client, companyId, employeeId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        var assetReturnTask = Assert.Single(overview.Tasks, t => t.Title.Contains("Laptop"));

        // Complete every other task (document review + manager exit checklist) but deliberately
        // leave the asset-return task outstanding.
        foreach (var task in overview.Tasks.Where(t => t.Id != assetReturnTask.Id))
        {
            var taskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, task.Id);
            var resp = await client.PostAsJsonAsync(
                $"/api/companies/{companyId}/tasks/{taskItemId}/complete",
                new { companyId, id = taskItemId });
            resp.EnsureSuccessStatusCode();
        }

        var overviewStillOpen = await GetOverviewAsync(client, companyId, employeeId);
        Assert.Equal("InProgress", overviewStillOpen.PlanStatus);

        // Now resolve the last outstanding task — the plan must complete.
        var assetTaskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, assetReturnTask.Id);
        var completeAssetResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/tasks/{assetTaskItemId}/complete",
            new { companyId, id = assetTaskItemId });
        completeAssetResp.EnsureSuccessStatusCode();

        var overviewCompleted = await GetOverviewAsync(client, companyId, employeeId);
        Assert.Equal("Completed", overviewCompleted.PlanStatus);
    }

    [Fact]
    public async Task CancellingLeavingProcess_Leaves_The_Asset_Assignment_Untouched()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var (assetId, _) = await AssignAssetAsync(client, companyId, employeeId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        var assetReturnTask = Assert.Single(overview.Tasks, t => t.Title.Contains("Laptop"));
        Assert.Equal("Pending", assetReturnTask.Status);

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });
        cancelResponse.EnsureSuccessStatusCode();

        var overviewAfter = await GetOverviewAsync(client, companyId, employeeId);
        Assert.Equal("Cancelled", overviewAfter.PlanStatus);
        var assetReturnTaskAfter = Assert.Single(overviewAfter.Tasks, t => t.Id == assetReturnTask.Id);
        Assert.Equal("Skipped", assetReturnTaskAfter.Status);

        // The core assertion: the underlying Assets-module assignment must be completely untouched —
        // still assigned/active, not silently returned as a side effect of cancellation.
        var assetAfter = await client.GetFromJsonAsync<AssetPayload>($"/api/companies/{companyId}/assets/{assetId}");
        Assert.Equal("Assigned", assetAfter!.Status);

        var employeeAssets = await client.GetFromJsonAsync<List<EmployeeAssetPayload>>(
            $"/api/companies/{companyId}/employees/{employeeId}/assets");
        Assert.NotNull(employeeAssets);
        Assert.Single(employeeAssets!);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record AssetPayload(Guid Id, string Status);

    private sealed record EmployeeAssetPayload(Guid Id, Guid AssetId, Guid EmployeeId);

    private sealed record OffboardingOverviewPayload(
        Guid EmployeeId,
        bool HasPlan,
        string? PlanStatus,
        DateOnly? LastWorkingDay,
        string? Notes,
        IReadOnlyList<OffboardingTaskOverviewItemPayload> Tasks);

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

    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskItemPayload> Items);

    private sealed record UnassignedTaskItemPayload(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        string Status,
        string Priority,
        string Source,
        string ActionType,
        DateOnly? DueDate,
        Guid? SourceEntityId,
        Guid CreatedBy,
        DateTimeOffset CreatedAt);

    private sealed record EmployeeTasksPayload(IReadOnlyList<EmployeeTaskItemPayload> Items);

    private sealed record EmployeeTaskItemPayload(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        string Status,
        string Priority,
        string Source,
        string ActionType,
        DateOnly? DueDate,
        Guid? AssignedEmployeeId,
        Guid? AssignedUserId,
        string? AssignedEmployeeName,
        Guid CreatedBy,
        Guid? CompletedBy,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? SourceEntityId);
}
