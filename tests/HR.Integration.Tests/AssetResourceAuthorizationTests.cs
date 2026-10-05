using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class AssetResourceAuthorizationTests(ApiWebApplicationFactory factory)
{
    // Pre-seeded company used by other tests; reused here to avoid re-seeding reference data.
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002");


    [Fact]
    public async Task GetAsset_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAsset_Allows_HrAdministrator_Viewing_Any_Asset()
    {
        var employee = await CreateEmployeeAsync();
        var assetId = await CreateAssetForEmployeeAsync(employee);

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{assetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = await response.Content.ReadFromJsonAsync<AssetDetailPayload>();
        Assert.NotNull(asset);
        Assert.Equal(assetId, asset.Id);
    }

    [Fact]
    public async Task GetAsset_Allows_Employee_Viewing_Own_Assigned_Asset()
    {
        var employee = await CreateEmployeeAsync();
        var assetId = await CreateAssetForEmployeeAsync(employee);

        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{assetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = await response.Content.ReadFromJsonAsync<AssetDetailPayload>();
        Assert.NotNull(asset);
        Assert.Equal(assetId, asset.Id);
    }

    [Fact]
    public async Task GetAsset_Allows_Direct_Manager_Viewing_Report_Asset()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();
        var assetId = await CreateAssetForEmployeeAsync(report);

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{assetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = await response.Content.ReadFromJsonAsync<AssetDetailPayload>();
        Assert.NotNull(asset);
        Assert.Equal(assetId, asset.Id);
    }

    [Fact]
    public async Task GetAsset_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();
        var assetId = await CreateAssetForEmployeeAsync(employee);

        using var peerClient = await AuthenticatedClient(peer);

        var response = await peerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{assetId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAsset_Returns_Forbidden_For_Unrelated_Manager()
    {
        var employee = await CreateEmployeeAsync();
        var otherManager = await CreateEmployeeAsync();
        var assetId = await CreateAssetForEmployeeAsync(employee);

        using var managerClient = await AuthenticatedClient(otherManager);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{assetId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAsset_Returns_Forbidden_For_Manager_Viewing_Unassigned_Asset()
    {
        var manager = await CreateEmployeeAsync();
        var unassignedAssetId = await CreateUnassignedAssetAsync();

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{unassignedAssetId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAsset_Allows_HrAdministrator_Viewing_Unassigned_Asset()
    {
        var unassignedAssetId = await CreateUnassignedAssetAsync();

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{unassignedAssetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = await response.Content.ReadFromJsonAsync<AssetDetailPayload>();
        Assert.NotNull(asset);
        Assert.Equal(unassignedAssetId, asset.Id);
    }

    [Fact]
    public async Task GetAsset_Returns_Forbidden_For_Employee_Viewing_Own_Manager_Asset()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();
        var managerAssetId = await CreateAssetForEmployeeAsync(manager);

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var reportClient = await AuthenticatedClient(report);

        var response = await reportClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/assets/{managerAssetId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    [Fact]
    public async Task ListEmployeeAssets_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/assets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListEmployeeAssets_Allows_Employee_Listing_Own_Assets()
    {
        var employee = await CreateEmployeeAsync();
        await CreateAssetForEmployeeAsync(employee);
        await CreateAssetForEmployeeAsync(employee);

        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<EmployeeAssetPayload>>();
        Assert.NotNull(assets);
        Assert.Equal(2, assets.Count);
    }

    [Fact]
    public async Task ListEmployeeAssets_Allows_Direct_Manager_Listing_Report_Assets()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();
        await CreateAssetForEmployeeAsync(report);

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<EmployeeAssetPayload>>();
        Assert.NotNull(assets);
        Assert.Single(assets);
    }

    [Fact]
    public async Task ListEmployeeAssets_Allows_HrAdministrator_Listing_Any_Employee_Assets()
    {
        var employee = await CreateEmployeeAsync();
        await CreateAssetForEmployeeAsync(employee);

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<EmployeeAssetPayload>>();
        Assert.NotNull(assets);
        Assert.Single(assets);
    }

    [Fact]
    public async Task ListEmployeeAssets_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();
        await CreateAssetForEmployeeAsync(employee);

        using var peerClient = await AuthenticatedClient(peer);

        var response = await peerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/assets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListEmployeeAssets_Returns_Forbidden_For_Unrelated_Manager()
    {
        var employee = await CreateEmployeeAsync();
        var otherManager = await CreateEmployeeAsync();
        await CreateAssetForEmployeeAsync(employee);

        using var managerClient = await AuthenticatedClient(otherManager);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/assets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListEmployeeAssets_Returns_Empty_For_Employee_With_No_Assets()
    {
        var employee = await CreateEmployeeAsync();

        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<EmployeeAssetPayload>>();
        Assert.NotNull(assets);
        Assert.Empty(assets);
    }

    [Fact]
    public async Task ListEmployeeAssets_Returns_Forbidden_For_Employee_Listing_Own_Manager_Assets()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();
        await CreateAssetForEmployeeAsync(manager);

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var reportClient = await AuthenticatedClient(report);

        var response = await reportClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{manager}/assets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListEmployeeAssets_Manager_Cannot_List_Assets_Of_Non_Reports()
    {
        var manager = await CreateEmployeeAsync();
        var employee1 = await CreateEmployeeAsync();
        var employee2 = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee1, manager);
        }

        await CreateAssetForEmployeeAsync(employee2);

        using var managerClient = await AuthenticatedClient(manager);

        var response1 = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee1}/assets");
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var response2 = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee2}/assets");
        Assert.Equal(HttpStatusCode.Forbidden, response2.StatusCode);
    }


    private async Task<HttpClient> AuthenticatedClient(Guid userId, bool hrAdministrator = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, SeededCompanyId);

        if (hrAdministrator)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator, SeededCompanyId);

        return client;
    }

    private async Task<Guid> CreateEmployeeAsync()
    {
        using var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var unique = Guid.NewGuid().ToString("N")[..12];

        var response = await setupClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId = SeededCompanyId,
                firstName = "Test",
                lastName = $"Employee-{unique}",
                workEmail = $"asset.auth.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                gender = "Male",
                employeeNumber = $"AAS-{unique}",
                employmentTypeId = EmploymentTypeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        return payload!.Id;
    }

    private async Task<Guid> CreateAssetForEmployeeAsync(Guid employeeId)
    {
        using var adminClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var categoryResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/asset-categories",
            new { companyId = SeededCompanyId, name = $"Category-{Guid.NewGuid():N}" });
        categoryResp.EnsureSuccessStatusCode();
        var category = await categoryResp.Content.ReadFromJsonAsync<IdPayload>();

        var assetResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/assets",
            new
            {
                companyId = SeededCompanyId,
                assetNumber = $"AUTHTEST-{Guid.NewGuid():N}",
                categoryId = category!.Id,
                name = $"Test Asset {Guid.NewGuid():N}",
                manufacturer = "TestMfg",
                model = "Model-X"
            });
        assetResp.EnsureSuccessStatusCode();
        var asset = await assetResp.Content.ReadFromJsonAsync<IdPayload>();

        var assignResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/assets/{asset!.Id}/assignments",
            new
            {
                companyId = SeededCompanyId,
                assetId = asset.Id,
                employeeId,
                assignedBy = Guid.NewGuid()
            });
        assignResp.EnsureSuccessStatusCode();

        return asset.Id;
    }

    private async Task<Guid> CreateUnassignedAssetAsync()
    {
        using var adminClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var categoryResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/asset-categories",
            new { companyId = SeededCompanyId, name = $"Category-{Guid.NewGuid():N}" });
        categoryResp.EnsureSuccessStatusCode();
        var category = await categoryResp.Content.ReadFromJsonAsync<IdPayload>();

        var assetResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/assets",
            new
            {
                companyId = SeededCompanyId,
                assetNumber = $"UNASSIGNED-{Guid.NewGuid():N}",
                categoryId = category!.Id,
                name = $"Unassigned Asset {Guid.NewGuid():N}",
                manufacturer = "TestMfg",
                model = "Model-Y"
            });
        assetResp.EnsureSuccessStatusCode();
        var asset = await assetResp.Content.ReadFromJsonAsync<IdPayload>();

        return asset!.Id;
    }

    private async Task AssignManagerAsync(HttpClient client, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/manager",
            new { companyId = SeededCompanyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }


    private sealed record EmployeePayload(Guid Id);
    private sealed record IdPayload(Guid Id);
    private sealed record AssetDetailPayload(Guid Id, string Status);
    private sealed record EmployeeAssetPayload(
        Guid Id,
        Guid AssetId,
        Guid EmployeeId,
        Guid AssignedBy,
        DateTime AssignedAt,
        string? Notes,
        string AssetNumber,
        string Name,
        string? Manufacturer,
        string? Model,
        string? SerialNumber,
        string? CategoryName,
        bool IsAcknowledged);
}
