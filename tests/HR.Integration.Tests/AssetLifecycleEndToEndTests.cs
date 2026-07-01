using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the complete asset lifecycle as a single coherent flow:
///
///   Create category → Create asset → Assign to employee
///     → Acknowledge task + notification created
///     → Complete acknowledgement → Return task auto-created
///     → Complete return → Asset back to Available, assignment inactive
/// </summary>
public class AssetLifecycleEndToEndTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("a1a1a1a1-0000-0000-0000-000000000001");

    public AssetLifecycleEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Full_Asset_Lifecycle_From_Assignment_To_Return()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var adminClient    = AuthenticatedClient(AdminUser, companyId);
        using var employeeClient = AuthenticatedClient(employeeId, companyId);

        // ── Step 1: Create asset category and asset ────────────────────────────
        var categoryResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "IT Equipment" });
        categoryResp.EnsureSuccessStatusCode();
        var category = await categoryResp.Content.ReadFromJsonAsync<IdPayload>();

        var assetResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets",
            new
            {
                companyId,
                assetNumber  = $"LIFE-{Guid.NewGuid():N}",
                categoryId   = category!.Id,
                name         = "MacBook Pro",
                manufacturer = "Apple",
                model        = "MacBook Pro 14-inch M3"
            });
        assetResp.EnsureSuccessStatusCode();
        var asset = await assetResp.Content.ReadFromJsonAsync<AssetPayload>();
        Assert.Equal("Available", asset!.Status);

        // ── Step 2: Assign asset to employee ───────────────────────────────────
        var assignResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset.Id}/assignments",
            new { companyId, assetId = asset.Id, employeeId, assignedBy = AdminUser });
        assignResp.EnsureSuccessStatusCode();
        var assignment = await assignResp.Content.ReadFromJsonAsync<AssignmentPayload>();

        // Asset should now be Assigned.
        var assetAfterAssign = await adminClient.GetFromJsonAsync<AssetPayload>(
            $"/api/companies/{companyId}/assets/{asset.Id}");
        Assert.Equal("Assigned", assetAfterAssign!.Status);

        // ── Step 3: Verify acknowledgement task was created for employee ───────
        var tasksAfterAssign = await GetEmployeeTasksAsync(adminClient, companyId, employeeId);
        var ackTask = Assert.Single(tasksAfterAssign, t => t.Source == "Asset" && t.ActionType == "Acknowledge");
        Assert.Equal("Open",   ackTask.Status);
        Assert.Equal(assignment!.Id, ackTask.SourceEntityId);

        // ── Step 4: Verify notification was sent to employee ──────────────────
        var notifications = await employeeClient.GetFromJsonAsync<NotificationListPayload>(
            $"/api/companies/{companyId}/notifications/my");
        Assert.True(notifications!.UnreadCount >= 1,
            "Expected at least one notification for the employee after asset assignment task");
        Assert.Contains(notifications.Items, n => n.Type == "TaskAssigned" && !n.IsRead);

        // ── Step 5: Employee completes the acknowledgement task ────────────────
        var ackCompleteResp = await employeeClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{ackTask.Id}/complete",
            EmptyJson());
        ackCompleteResp.EnsureSuccessStatusCode();

        // Assignment should now be acknowledged.
        var activeAssets = await adminClient.GetFromJsonAsync<List<EmployeeAssetPayload>>(
            $"/api/companies/{companyId}/employees/{employeeId}/assets");
        Assert.Single(activeAssets!);
        Assert.True(activeAssets![0].IsAcknowledged,
            "Expected assignment to be marked acknowledged after task completion");

        // ── Step 6: Verify return task was auto-created after acknowledgement ──
        var tasksAfterAck = await GetEmployeeTasksAsync(adminClient, companyId, employeeId);
        var returnTask = Assert.Single(tasksAfterAck, t => t.Source == "Asset" && t.ActionType == "Return");
        Assert.Equal("Open", returnTask.Status);
        Assert.Equal(assignment.Id, returnTask.SourceEntityId);

        // ── Step 7: Employee completes the return task ─────────────────────────
        var returnCompleteResp = await employeeClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{returnTask.Id}/complete",
            EmptyJson());
        returnCompleteResp.EnsureSuccessStatusCode();

        // ── Step 8: Assignment should be inactive (no active assets) ──────────
        var activeAssetsAfterReturn = await adminClient.GetFromJsonAsync<List<EmployeeAssetPayload>>(
            $"/api/companies/{companyId}/employees/{employeeId}/assets");
        Assert.Empty(activeAssetsAfterReturn!);

        // ── Step 9: Asset status should be back to Available ──────────────────
        var assetAfterReturn = await adminClient.GetFromJsonAsync<AssetPayload>(
            $"/api/companies/{companyId}/assets/{asset.Id}");
        Assert.Equal("Available", assetAfterReturn!.Status);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static async Task<List<TaskItem>> GetEmployeeTasksAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var resp = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks");
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<TaskListPayload>();
        return payload!.Items.ToList();
    }

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

    private sealed record IdPayload(Guid Id);
    private sealed record AssetPayload(Guid Id, string Status);
    private sealed record AssignmentPayload(Guid Id);
    private sealed record EmployeeAssetPayload(Guid Id, Guid AssetId, bool IsAcknowledged);
    private sealed record TaskListPayload(IReadOnlyList<TaskItem> Items);
    private sealed record TaskItem(Guid Id, string Source, string ActionType, string Status, Guid? SourceEntityId);
    private sealed record NotificationListPayload(int UnreadCount, IReadOnlyList<NotifItem> Items);
    private sealed record NotifItem(Guid Id, bool IsRead, string Type);
}
