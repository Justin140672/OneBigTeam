using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies that completing a task whose Source is Asset and ActionType is Return
/// calls AssetReturnService, marking the assignment inactive and returning the asset
/// status to Available.
/// </summary>
[Collection("Integration")]
public class AssetReturnFromTaskEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("f2000001-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("f2000001-0000-0000-0000-000000000002");

    public AssetReturnFromTaskEndToEndTests(ApiWebApplicationFactory factory)
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
    public async Task CompleteReturnTask_Removes_Assignment_From_Employee_Active_Assets()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User1, companyId);

        var (assetId, assignmentId) = await CreateActiveAssignmentAsync(client, companyId, employeeId);

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title:              "Return asset",
            source:             TaskSource.Asset,
            actionType:         TaskActionType.Return,
            assignedEmployeeId: employeeId,
            sourceEntityId:     assignmentId);

        var assetsBeforeReturn = await client.GetFromJsonAsync<List<EmployeeAssetPayload>>(
            $"/api/companies/{companyId}/employees/{employeeId}/assets");
        Assert.NotNull(assetsBeforeReturn);
        Assert.Single(assetsBeforeReturn!);

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{taskId}/complete",
            EmptyJson());
        completeResp.EnsureSuccessStatusCode();

        var assetsAfterReturn = await client.GetFromJsonAsync<List<EmployeeAssetPayload>>(
            $"/api/companies/{companyId}/employees/{employeeId}/assets");
        Assert.NotNull(assetsAfterReturn);
        Assert.Empty(assetsAfterReturn!);
    }

    [Fact]
    public async Task CompleteReturnTask_Marks_Asset_Status_As_Available()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User2, companyId);

        var (assetId, assignmentId) = await CreateActiveAssignmentAsync(client, companyId, employeeId);

        var assetBeforeReturn = await client.GetFromJsonAsync<AssetPayload>(
            $"/api/companies/{companyId}/assets/{assetId}");
        Assert.Equal("Assigned", assetBeforeReturn!.Status);

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title:              "Return asset",
            source:             TaskSource.Asset,
            actionType:         TaskActionType.Return,
            assignedEmployeeId: employeeId,
            sourceEntityId:     assignmentId);

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{taskId}/complete",
            EmptyJson());
        completeResp.EnsureSuccessStatusCode();

        var assetAfterReturn = await client.GetFromJsonAsync<AssetPayload>(
            $"/api/companies/{companyId}/assets/{assetId}");
        Assert.Equal("Available", assetAfterReturn!.Status);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task<(Guid assetId, Guid assignmentId)> CreateActiveAssignmentAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var categoryResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "Electronics" });
        categoryResp.EnsureSuccessStatusCode();
        var category = await categoryResp.Content.ReadFromJsonAsync<IdPayload>();

        var assetResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets",
            new { companyId, assetNumber = $"RET-{Guid.NewGuid():N}", categoryId = category!.Id, name = "Laptop" });
        assetResp.EnsureSuccessStatusCode();
        var asset = await assetResp.Content.ReadFromJsonAsync<IdPayload>();

        var assignResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset!.Id}/assignments",
            new { companyId, assetId = asset.Id, employeeId, assignedBy = Guid.NewGuid() });
        assignResp.EnsureSuccessStatusCode();
        var assignment = await assignResp.Content.ReadFromJsonAsync<IdPayload>();

        return (asset.Id, assignment!.Id);
    }

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

    private sealed record IdPayload(Guid Id);

    private sealed record EmployeeAssetPayload(Guid Id, Guid AssetId, Guid EmployeeId);

    private sealed record AssetPayload(Guid Id, string Status);
}
