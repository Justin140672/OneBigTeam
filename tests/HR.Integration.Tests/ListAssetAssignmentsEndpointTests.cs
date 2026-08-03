using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListAssetAssignmentsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000011-0000-0000-0000-000000000098");

    public ListAssetAssignmentsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Electronics"
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private async Task<Guid> CreateAssetAsync(HttpClient client, Guid companyId, Guid categoryId, string assetNumber)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber,
            categoryId,
            name = $"Asset {assetNumber}"
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AssetIdPayload>();
        return payload!.Id;
    }

    private async Task<Guid> AssignAssetAsync(HttpClient client, Guid companyId, Guid assetId, Guid employeeId, string? notes = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments",
            new
            {
                companyId,
                assetId,
                employeeId,
                assignedBy = AdminUserId,
                notes
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AssignmentIdPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task List_Asset_Assignments_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/assets/{Guid.NewGuid()}/assignments");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Asset_Assignments_Returns_Empty_List_When_No_Assignments_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var categoryId = await CreateCategoryAsync(client, companyId);
        var assetId = await CreateAssetAsync(client, companyId, categoryId, $"LA-{Guid.NewGuid():N}");

        var response = await client.GetAsync($"/api/companies/{companyId}/assets/{assetId}/assignments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssignmentPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    [Fact]
    public async Task List_Asset_Assignments_Returns_Assignment_After_Assigning()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var categoryId = await CreateCategoryAsync(client, companyId);
        var assetId = await CreateAssetAsync(client, companyId, categoryId, $"LA-{Guid.NewGuid():N}");
        var assignmentId = await AssignAssetAsync(client, companyId, assetId, employeeId, "Test notes");

        var response = await client.GetAsync($"/api/companies/{companyId}/assets/{assetId}/assignments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssignmentPayload>>();
        Assert.NotNull(payload);
        Assert.Single(payload!);
        var item = payload![0];
        Assert.Equal(assignmentId, item.Id);
        Assert.Equal(assetId, item.AssetId);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal("Test notes", item.Notes);
        Assert.True(item.IsActive);
        Assert.Null(item.ReturnedAt);
    }

    [Fact]
    public async Task List_Asset_Assignments_Does_Not_Return_Assignments_For_Other_Assets()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var categoryId = await CreateCategoryAsync(client, companyId);
        var assetId1 = await CreateAssetAsync(client, companyId, categoryId, $"LA1-{Guid.NewGuid():N}");
        var assetId2 = await CreateAssetAsync(client, companyId, categoryId, $"LA2-{Guid.NewGuid():N}");
        await AssignAssetAsync(client, companyId, assetId1, employeeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/assets/{assetId2}/assignments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssignmentPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    [Fact]
    public async Task List_Asset_Assignments_Returns_All_Assignments_Including_Returned()
    {
        var companyId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var categoryId = await CreateCategoryAsync(client, companyId);

        // Create two separate assets so we can assign the same asset twice in sequence
        // (after returning the first assignment, reassigning requires Available status)
        var assetId = await CreateAssetAsync(client, companyId, categoryId, $"LAR-{Guid.NewGuid():N}");
        var assignmentId = await AssignAssetAsync(client, companyId, assetId, employeeId1);

        // Request return and complete it to restore Available status
        var requestReturnResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-assignments/{assignmentId}/request-return",
            new { companyId, id = assignmentId, requestedBy = AdminUserId });
        requestReturnResp.EnsureSuccessStatusCode();

        var tasksResp = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId1}/tasks");
        var tasksPayload = await tasksResp.Content.ReadFromJsonAsync<TaskListPayload>();
        var returnTask = tasksPayload!.Items.FirstOrDefault(t => t.ActionType == "Return");
        Assert.NotNull(returnTask);

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{returnTask!.Id}/complete",
            new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        completeResp.EnsureSuccessStatusCode();

        // Reassign the now-available asset to a second employee
        await AssignAssetAsync(client, companyId, assetId, employeeId2);

        var response = await client.GetAsync($"/api/companies/{companyId}/assets/{assetId}/assignments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<AssignmentPayload>>();
        Assert.NotNull(payload);
        // Both assignments (returned + active) should be present
        Assert.Equal(2, payload!.Count);

        var returned = payload.FirstOrDefault(a => a.EmployeeId == employeeId1);
        var active   = payload.FirstOrDefault(a => a.EmployeeId == employeeId2);
        Assert.NotNull(returned);
        Assert.NotNull(active);
        Assert.NotNull(returned!.ReturnedAt);
        Assert.True(active!.IsActive);
    }

    private sealed record CategoryPayload(Guid Id);
    private sealed record AssetIdPayload(Guid Id);
    private sealed record AssignmentIdPayload(Guid Id);
    private sealed record TaskListPayload(IReadOnlyList<TaskItemPayload> Items);
    private sealed record TaskItemPayload(Guid Id, string ActionType, string Status);

    private sealed record AssignmentPayload(
        Guid Id,
        Guid CompanyId,
        Guid AssetId,
        Guid EmployeeId,
        Guid AssignedBy,
        DateTimeOffset AssignedAt,
        DateTimeOffset? AcknowledgedAt,
        DateTimeOffset? ReturnedAt,
        string? Notes,
        bool IsActive);
}
