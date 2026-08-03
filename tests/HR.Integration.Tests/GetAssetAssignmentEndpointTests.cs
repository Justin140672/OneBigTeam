using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetAssetAssignmentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000005-0000-0000-0000-000000000099");

    public GetAssetAssignmentEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<(Guid assetId, Guid assignmentId)> CreateAssignmentAsync(HttpClient client, Guid companyId)
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
        var assetId = asset!.Id;

        var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments",
            new
            {
                companyId,
                assetId,
                employeeId = Guid.NewGuid(),
                assignedBy = Guid.NewGuid(),
                notes = "Test notes"
            });
        assignmentResponse.EnsureSuccessStatusCode();
        var assignment = await assignmentResponse.Content.ReadFromJsonAsync<AssignmentPayload>();

        return (assetId, assignment!.Id);
    }

    [Fact]
    public async Task Get_AssetAssignment_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/assets/{Guid.NewGuid()}/assignments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AssetAssignment_Returns_NotFound_When_Assignment_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/assets/{Guid.NewGuid()}/assignments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_AssetAssignment_Returns_Assignment_When_Found()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var (assetId, assignmentId) = await CreateAssignmentAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments/{assignmentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AssignmentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(assignmentId, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(assetId, payload.AssetId);
        Assert.True(payload.IsActive);
        Assert.Null(payload.ReturnedAt);
        Assert.Equal("Test notes", payload.Notes);
    }

    [Fact]
    public async Task Get_AssetAssignment_Returns_NotFound_When_Assignment_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        using var otherClient = AdminClient(otherCompanyId);

        var (assetId, assignmentId) = await CreateAssignmentAsync(client, companyId);

        var response = await otherClient.GetAsync(
            $"/api/companies/{otherCompanyId}/assets/{assetId}/assignments/{assignmentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_AssetAssignment_Returns_NotFound_When_AssetId_Does_Not_Match()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var (_, assignmentId) = await CreateAssignmentAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/assets/{Guid.NewGuid()}/assignments/{assignmentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        DateTimeOffset? ReturnedAt,
        string? Notes,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        bool IsActive);
}
