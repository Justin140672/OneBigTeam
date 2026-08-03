using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RequestAssetReturnEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000005-0000-0000-0000-000000000099");

    public RequestAssetReturnEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<(Guid assetId, Guid assignmentId)> CreateActiveAssignmentAsync(HttpClient client, Guid companyId)
    {
        var categoryResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Electronics"
        });
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryPayload>();

        var assetResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets", new
        {
            companyId,
            assetNumber = $"ASSET-{Guid.NewGuid():N}",
            categoryId = category!.Id,
            name = "Laptop"
        });
        assetResponse.EnsureSuccessStatusCode();
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetPayload>();

        var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset!.Id}/assignments",
            new
            {
                companyId,
                assetId = asset.Id,
                employeeId = Guid.NewGuid(),
                assignedBy = AdminUserId
            });
        assignmentResponse.EnsureSuccessStatusCode();
        var assignment = await assignmentResponse.Content.ReadFromJsonAsync<AssignmentPayload>();

        return (asset.Id, assignment!.Id);
    }

    [Fact]
    public async Task Post_RequestAssetReturn_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-assignments/{Guid.NewGuid()}/request-return",
            new { requestedBy = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequestAssetReturn_Returns_NoContent_For_Active_Assignment()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var (_, assignmentId) = await CreateActiveAssignmentAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-assignments/{assignmentId}/request-return",
            new { companyId, id = assignmentId, requestedBy = AdminUserId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequestAssetReturn_Returns_NotFound_When_Assignment_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-assignments/{Guid.NewGuid()}/request-return",
            new { companyId, id = Guid.NewGuid(), requestedBy = AdminUserId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name);
    private sealed record AssetPayload(Guid Id, Guid CompanyId, string AssetNumber, string Status);
    private sealed record AssignmentPayload(Guid Id, Guid CompanyId, Guid AssetId, Guid EmployeeId, Guid AssignedBy, DateTimeOffset AssignedAt, string? Notes, DateTimeOffset CreatedAt);
}
