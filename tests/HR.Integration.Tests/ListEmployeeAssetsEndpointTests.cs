using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListEmployeeAssetsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000011-0000-0000-0000-000000000099");

    public ListEmployeeAssetsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
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

    private async Task<Guid> CreateAssetAsync(HttpClient client, Guid companyId, Guid categoryId, string assetNumber = "A001")
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

    private async Task<Guid> AssignAssetAsync(HttpClient client, Guid companyId, Guid assetId, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments",
            new
            {
                companyId,
                assetId,
                employeeId,
                assignedBy = Guid.NewGuid(),
                notes = "Integration test assignment"
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AssignmentIdPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Get_Employee_Assets_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/assets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Assets_Returns_Empty_List_When_No_Assignments_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{Guid.NewGuid()}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<EmployeeAssetPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    [Fact]
    public async Task Get_Employee_Assets_Returns_Active_Assignments_For_Employee()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var categoryId = await CreateCategoryAsync(client, companyId);
        var assetId = await CreateAssetAsync(client, companyId, categoryId, $"EMP-{Guid.NewGuid():N}");
        await AssignAssetAsync(client, companyId, assetId, employeeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<EmployeeAssetPayload>>();
        Assert.NotNull(payload);
        Assert.Single(payload!);
        Assert.Equal(employeeId, payload![0].EmployeeId);
        Assert.Equal(assetId, payload[0].AssetId);
    }

    [Fact]
    public async Task Get_Employee_Assets_Does_Not_Return_Assets_Assigned_To_Other_Employees()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var categoryId = await CreateCategoryAsync(client, companyId);
        var assetId = await CreateAssetAsync(client, companyId, categoryId, $"OTH-{Guid.NewGuid():N}");
        await AssignAssetAsync(client, companyId, assetId, otherEmployeeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<EmployeeAssetPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    [Fact]
    public async Task Get_Employee_Assets_Includes_Asset_Details_In_Response()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var categoryId = await CreateCategoryAsync(client, companyId);
        var assetNumber = $"DET-{Guid.NewGuid():N}";
        var assetId = await CreateAssetAsync(client, companyId, categoryId, assetNumber);
        await AssignAssetAsync(client, companyId, assetId, employeeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<EmployeeAssetPayload>>();
        Assert.NotNull(payload);
        Assert.Single(payload!);
        var item = payload![0];
        Assert.Equal(assetId, item.AssetId);
        Assert.Equal(assetNumber, item.AssetNumber);
        Assert.NotNull(item.Name);
        Assert.NotEqual(default, item.AssignedAt);
    }

    [Fact]
    public async Task Get_Employee_Assets_Excludes_Returned_Assignments()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var categoryId   = await CreateCategoryAsync(client, companyId);
        var assetId      = await CreateAssetAsync(client, companyId, categoryId, $"RET-{Guid.NewGuid():N}");
        var assignmentId = await AssignAssetAsync(client, companyId, assetId, employeeId);

        // Verify the assignment shows before return.
        var before = await client.GetFromJsonAsync<List<EmployeeAssetPayload>>(
            $"/api/companies/{companyId}/employees/{employeeId}/assets");
        Assert.Single(before!);

        // Request return and complete via task completion flow — or use the request-return endpoint.
        var requestReturnResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-assignments/{assignmentId}/request-return",
            new { companyId, id = assignmentId, requestedBy = AdminUserId });
        requestReturnResp.EnsureSuccessStatusCode();

        // Complete the return task that was created.
        var tasksResp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/tasks");
        var tasksPayload = await tasksResp.Content.ReadFromJsonAsync<TaskListPayload>();
        var returnTask = tasksPayload!.Items.FirstOrDefault(t => t.ActionType == "Return");
        Assert.NotNull(returnTask);

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{returnTask!.Id}/complete",
            new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        completeResp.EnsureSuccessStatusCode();

        // Assignment should no longer appear since it is no longer active.
        var after = await client.GetFromJsonAsync<List<EmployeeAssetPayload>>(
            $"/api/companies/{companyId}/employees/{employeeId}/assets");
        Assert.NotNull(after);
        Assert.Empty(after!);
    }

    private sealed record CategoryPayload(Guid Id);
    private sealed record AssetIdPayload(Guid Id);
    private sealed record AssignmentIdPayload(Guid Id);
    private sealed record TaskListPayload(IReadOnlyList<TaskItemPayload> Items);
    private sealed record TaskItemPayload(Guid Id, string ActionType, string Status);

    private sealed record EmployeeAssetPayload(
        Guid Id,
        Guid AssetId,
        Guid EmployeeId,
        Guid AssignedBy,
        DateTimeOffset AssignedAt,
        string? Notes,
        string AssetNumber,
        string Name,
        string? Manufacturer,
        string? Model,
        string? SerialNumber);
}
