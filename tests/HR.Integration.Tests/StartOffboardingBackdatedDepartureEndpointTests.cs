using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// OFF-05: backdated-departure-specific coverage for StartOffboarding. 401/validation-failure
// coverage for this endpoint already exists in StartOffboardingEndpointTests — deliberately not
// duplicated here.
[Collection("Integration")]
public class StartOffboardingBackdatedDepartureEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("ff050001-0000-0000-0000-000000000001");

    public StartOffboardingBackdatedDepartureEndpointTests(ApiWebApplicationFactory factory)
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

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Off05", "Employee", $"off05.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task<Guid> AssignAssetAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var categoryResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories",
            new { companyId, name = "Electronics" });
        categoryResp.EnsureSuccessStatusCode();
        var category = await categoryResp.Content.ReadFromJsonAsync<IdPayload>();

        var assetResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/assets",
            new { companyId, assetNumber = $"OFF05-{Guid.NewGuid():N}", categoryId = category!.Id, name = "Laptop" });
        assetResp.EnsureSuccessStatusCode();
        var asset = await assetResp.Content.ReadFromJsonAsync<IdPayload>();

        var assignResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{asset!.Id}/assignments",
            new { companyId, assetId = asset.Id, employeeId, assignedBy = Guid.NewGuid() });
        assignResp.EnsureSuccessStatusCode();
        var assignment = await assignResp.Content.ReadFromJsonAsync<IdPayload>();

        return asset.Id;
    }

    private static async Task<OffboardingOverviewPayload> GetOverviewAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OffboardingOverviewPayload>())!;
    }

    [Fact]
    public async Task Post_StartOffboarding_With_Backdated_LastWorkingDay_And_AutoDisableAccess_Marks_Plan_For_Reconciliation()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        await AssignAssetAsync(client, companyId, employeeId);

        // New companies default AutoDisableAccessOnLeavingDate to true (see CompanySettings.Create),
        // so no explicit PUT to /hr-settings is needed here.
        var backdatedLastWorkingDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = backdatedLastWorkingDay.ToString("yyyy-MM-dd") });
        response.EnsureSuccessStatusCode();

        var overview = await GetOverviewAsync(client, companyId, employeeId);

        Assert.True(overview.HasPlan);
        Assert.True(overview.IsBackdated);
        Assert.True(overview.RequiresHrReconciliation);

        var assetTask = Assert.Single(overview.Tasks, t => t.Title.Contains("Laptop"));
        Assert.Equal("HR", assetTask.AssignTo);
        Assert.True(assetTask.RequiresHrConfirmation);
    }

    [Fact]
    public async Task Post_StartOffboarding_With_Future_LastWorkingDay_Is_Not_Backdated_And_Assigns_Asset_To_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);
        await AssignAssetAsync(client, companyId, employeeId);

        var futureLastWorkingDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = futureLastWorkingDay.ToString("yyyy-MM-dd") });
        response.EnsureSuccessStatusCode();

        var overview = await GetOverviewAsync(client, companyId, employeeId);

        Assert.True(overview.HasPlan);
        Assert.False(overview.IsBackdated);
        Assert.False(overview.RequiresHrReconciliation);

        var assetTask = Assert.Single(overview.Tasks, t => t.Title.Contains("Laptop"));
        Assert.Equal("Employee", assetTask.AssignTo);
        Assert.False(assetTask.RequiresHrConfirmation);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OffboardingOverviewPayload(
        Guid EmployeeId,
        bool HasPlan,
        string? PlanStatus,
        DateOnly? LastWorkingDay,
        string? Notes,
        bool IsBackdated,
        bool RequiresHrReconciliation,
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
        DateTimeOffset UpdatedAt,
        bool RequiresHrConfirmation);
}
