using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies that completing a task whose Source is Asset and ActionType is Acknowledge
/// calls AssetAcknowledgementService, marking the assignment as acknowledged and then
/// creating a follow-up Return task.
/// </summary>
[Collection("Integration")]
public class AssetAcknowledgementFromTaskEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("f1000001-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("f1000001-0000-0000-0000-000000000002");

    public AssetAcknowledgementFromTaskEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task CompleteAcknowledgeTask_Marks_Assignment_As_Acknowledged()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AuthenticatedClient(User1, companyId);

        var assignmentId = await CreateActiveAssignmentAsync(client, companyId, employeeId);

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title:              "Acknowledge receipt of asset",
            source:             TaskSource.Asset,
            actionType:         TaskActionType.Acknowledge,
            assignedEmployeeId: employeeId,
            sourceEntityId:     assignmentId);

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{taskId}/complete",
            EmptyJson());
        completeResp.EnsureSuccessStatusCode();

        var assets = await client.GetFromJsonAsync<List<EmployeeAssetPayload>>(
            $"/api/companies/{companyId}/employees/{employeeId}/assets");

        Assert.NotNull(assets);
        var assignment = Assert.Single(assets!);
        Assert.True(assignment.IsAcknowledged,
            "Expected the assignment to be marked as acknowledged after task completion.");
    }

    [Fact]
    public async Task CompleteAcknowledgeTask_Creates_Follow_Up_Return_Task()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AuthenticatedClient(User2, companyId);

        var assignmentId = await CreateActiveAssignmentAsync(client, companyId, employeeId);

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title:              "Acknowledge receipt of asset",
            source:             TaskSource.Asset,
            actionType:         TaskActionType.Acknowledge,
            assignedEmployeeId: employeeId,
            sourceEntityId:     assignmentId);

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{taskId}/complete",
            EmptyJson());
        completeResp.EnsureSuccessStatusCode();

        var tasksResp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/tasks");
        tasksResp.EnsureSuccessStatusCode();

        var payload = await tasksResp.Content.ReadFromJsonAsync<TaskListPayload>();
        Assert.NotNull(payload);

        var returnTask = payload!.Items.FirstOrDefault(t =>
            t.Source == "Asset" && t.ActionType == "Return");

        Assert.NotNull(returnTask);
        Assert.Equal("Open", returnTask!.Status);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateActiveAssignmentAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var categoryResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "Electronics" });
        categoryResp.EnsureSuccessStatusCode();
        var category = await categoryResp.Content.ReadFromJsonAsync<IdPayload>();

        var assetResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets",
            new { companyId, assetNumber = $"ACK-{Guid.NewGuid():N}", categoryId = category!.Id, name = "Laptop" });
        assetResp.EnsureSuccessStatusCode();
        var asset = await assetResp.Content.ReadFromJsonAsync<IdPayload>();

        var assignResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset!.Id}/assignments",
            new { companyId, assetId = asset.Id, employeeId, assignedBy = Guid.NewGuid() });
        assignResp.EnsureSuccessStatusCode();
        var assignment = await assignResp.Content.ReadFromJsonAsync<IdPayload>();

        return assignment!.Id;
    }

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

    private sealed record IdPayload(Guid Id);

    private sealed record EmployeeAssetPayload(
        Guid Id, Guid AssetId, Guid EmployeeId, bool IsAcknowledged);

    private sealed record TaskListPayload(IReadOnlyList<TaskItemPayload> Items);

    private sealed record TaskItemPayload(
        Guid Id,
        string Title,
        string Status,
        string Source,
        string ActionType,
        Guid? AssignedEmployeeId,
        DateTimeOffset CreatedAt);
}
