using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateAssetAssignmentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000004-0000-0000-0000-000000000099");

    public CreateAssetAssignmentEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> CreateAssetAsync(HttpClient client, Guid companyId)
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
        return asset!.Id;
    }

    [Fact]
    public async Task Post_AssetAssignment_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{Guid.NewGuid()}/assignments",
            new { employeeId = Guid.NewGuid(), assignedBy = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_AssetAssignment_Returns_Created_For_Available_Asset()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var assetId = await CreateAssetAsync(client, companyId);
        var employeeId = Guid.NewGuid();
        var assignedBy = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments",
            new
            {
                companyId,
                assetId,
                employeeId,
                assignedBy,
                notes = "Handle with care"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<AssignmentPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(assetId, payload.AssetId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(assignedBy, payload.AssignedBy);
        Assert.Equal("Handle with care", payload.Notes);
    }

    [Fact]
    public async Task Post_AssetAssignment_Returns_NotFound_When_Asset_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{Guid.NewGuid()}/assignments",
            new
            {
                companyId,
                assetId = Guid.NewGuid(),
                employeeId = Guid.NewGuid(),
                assignedBy = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AssetAssignment_Returns_Conflict_When_Asset_Is_Not_Available()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var assetId = await CreateAssetAsync(client, companyId);

        // First assignment
        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments",
            new
            {
                companyId,
                assetId,
                employeeId = Guid.NewGuid(),
                assignedBy = Guid.NewGuid()
            });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Second assignment — asset is now Assigned
        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments",
            new
            {
                companyId,
                assetId,
                employeeId = Guid.NewGuid(),
                assignedBy = Guid.NewGuid()
            });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name);
    private sealed record AssetPayload(Guid Id, Guid CompanyId, string AssetNumber, string Status);
    private sealed record AssignmentPayload(
        Guid Id,
        Guid CompanyId,
        Guid AssetId,
        Guid EmployeeId,
        Guid AssignedBy,
        DateTimeOffset AssignedAt,
        string? Notes,
        DateTimeOffset CreatedAt);
}
