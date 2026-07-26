using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetOnboardingOverviewEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("ee000001-0000-0000-0000-000000000001");

    public GetOnboardingOverviewEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_OnboardingOverview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/onboarding-overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OnboardingOverview_Returns_HasPlan_False_When_Employee_Has_No_Plan()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/onboarding-overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.False(payload.HasPlan);
        Assert.Null(payload.PlanStatus);
        Assert.Null(payload.StartDate);
        Assert.Empty(payload.Tasks);
        Assert.Empty(payload.OutstandingDocumentRequests);
        Assert.Empty(payload.OutstandingAssetAcknowledgements);
        Assert.Null(payload.Probation);
    }

    [Fact]
    public async Task Get_OnboardingOverview_Returns_Plan_And_Default_Tasks_After_Employee_Created()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/onboarding-overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasPlan);
        Assert.Equal("NotStarted", payload.PlanStatus);
        Assert.NotNull(payload.StartDate);
        Assert.NotEmpty(payload.Tasks);
    }

    [Fact]
    public async Task Get_OnboardingOverview_Includes_Outstanding_Cross_Module_Sections()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Software Engineer");
        var (docTypeId, docTypeName) = await CreateDocumentTypeAsync(client, companyId, "Passport");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId, isMandatory: true, dueDays: 30);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var categoryId = await CreateAssetCategoryAsync(client, companyId, "IT Equipment");
        var assetId = await CreateAssetAsync(client, companyId, categoryId, $"OB-{Guid.NewGuid():N}");
        await AssignAssetAsync(client, companyId, assetId, employeeId);

        await CreateProbationRecordAsync(client, companyId, employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/onboarding-overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasPlan);

        var docRequest = Assert.Single(payload.OutstandingDocumentRequests);
        Assert.Equal(docTypeName, docRequest.DocumentTypeName);
        Assert.True(docRequest.IsMandatory);

        var assetAck = Assert.Single(payload.OutstandingAssetAcknowledgements);
        Assert.Equal(assetId, assetAck.AssetId);

        Assert.NotNull(payload.Probation);
        Assert.Equal("Active", payload.Probation!.Status);
    }

    [Fact]
    public async Task Get_OnboardingOverview_Surfaces_CompletedAt_For_Completed_Task_And_Null_For_Pending_Task()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId, positionProfileId: null);

        await CompleteUnassignedOnboardingTaskAsync(client, companyId, "Set up workstation");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/onboarding-overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OverviewPayload>();
        Assert.NotNull(payload);

        var completedTask = Assert.Single(payload!.Tasks, t => t.Title.Contains("Set up workstation"));
        Assert.Equal("Completed", completedTask.Status);
        Assert.NotNull(completedTask.CompletedAt);
        Assert.True(completedTask.CompletedAt >= completedTask.CreatedAt);
        Assert.Equal(completedTask.CompletedAt, completedTask.UpdatedAt);

        var pendingTask = Assert.Single(payload.Tasks, t => t.Title.Contains("Send welcome email"));
        Assert.Equal("Pending", pendingTask.Status);
        Assert.Null(pendingTask.CompletedAt);
        Assert.Equal(pendingTask.CreatedAt, pendingTask.UpdatedAt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    // Department/Location/EmploymentType/EmployeeNumber are all mandatory on employee creation —
    // seed fresh reference data per call. A null positionProfileId means "use a fresh, bare
    // position profile with no onboarding template/required documents of its own" (no employee
    // can be created without a real PositionProfileId any more).
    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId, Guid? positionProfileId = null)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var effectiveProfileId = positionProfileId ?? refData.PositionProfileId;

        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            BuildEmployeeBody(companyId, "Onboard", $"Employee{Guid.NewGuid():N}", effectiveProfileId, refData));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static object BuildEmployeeBody(
        Guid companyId, string firstName, string lastName, Guid positionProfileId,
        EmployeeReferenceDataSeeder.ReferenceData refData) =>
        new
        {
            companyId,
            firstName,
            lastName,
            workEmail = $"{firstName.ToLower()}.{lastName.ToLower()}@onboardtest.example",
            startDate = "2026-07-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            employmentTypeId = refData.EmploymentTypeId,
            positionProfileId,
        };

    private async Task<Guid> CreatePositionProfileAsync(HttpClient client, Guid companyId, string title)
    {
        var departmentId = await CreateDepartmentAsync(client, companyId);
        var locationId = await CreateLocationAsync(client, companyId);
        var leavePolicyId = await CreateLeavePolicyAsync(client, companyId);

        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, defaultLeavePolicyId = leavePolicyId, title });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task<Guid> CreateDepartmentAsync(HttpClient client, Guid companyId, string name = "Engineering")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}"
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client, Guid companyId, string name = "Head Office")
    {
        var locationTypeResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = $"Office Type {Guid.NewGuid():N}"
        });
        locationTypeResponse.EnsureSuccessStatusCode();
        var locationType = await locationTypeResponse.Content.ReadFromJsonAsync<IdPayload>();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}",
            locationTypeId = locationType!.Id
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static async Task<Guid> CreateLeavePolicyAsync(HttpClient client, Guid companyId, string name = "Standard Leave")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}",
            carryOverDays = 5,
            allowNegativeBalance = false
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private async Task<(Guid Id, string Name)> CreateDocumentTypeAsync(HttpClient client, Guid companyId, string namePrefix)
    {
        var name = $"{namePrefix} {Guid.NewGuid():N}";
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { companyId, name });
        resp.EnsureSuccessStatusCode();
        var id = (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
        return (id, name);
    }

    private async Task AddRequiredDocumentAsync(
        HttpClient client, Guid companyId, Guid profileId, Guid docTypeId,
        bool isMandatory, int? dueDays)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new { companyId, positionProfileId = profileId, documentTypeId = docTypeId, isMandatory, dueDaysAfterStart = dueDays });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateAssetCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories",
            new { companyId, name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<Guid> CreateAssetAsync(HttpClient client, Guid companyId, Guid categoryId, string assetNumber)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets",
            new { companyId, assetNumber, categoryId, name = $"Asset {assetNumber}" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task AssignAssetAsync(HttpClient client, Guid companyId, Guid assetId, Guid employeeId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/assets/{assetId}/assignments",
            new
            {
                companyId,
                assetId,
                employeeId,
                assignedBy = AdminUser,
                notes = "Integration test assignment",
            });
        resp.EnsureSuccessStatusCode();
    }

    private async Task CreateProbationRecordAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var resp = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01",
            notes = "Integration test.",
        });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CompleteUnassignedOnboardingTaskAsync(HttpClient client, Guid companyId, string titleContains)
    {
        var listResp = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        listResp.EnsureSuccessStatusCode();
        var payload = await listResp.Content.ReadFromJsonAsync<UnassignedTasksPayload>();
        var task = payload!.Items.Single(t => t.Source == "Onboarding" && t.Title.Contains(titleContains));

        var completeResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        completeResp.EnsureSuccessStatusCode();

        return task.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskPayload> Items);

    private sealed record UnassignedTaskPayload(Guid Id, string Title, string? Source);

    private sealed record OverviewPayload(
        Guid EmployeeId,
        bool HasPlan,
        string? PlanStatus,
        DateOnly? StartDate,
        List<OnboardingTaskItemPayload> Tasks,
        List<DocumentRequestItemPayload> OutstandingDocumentRequests,
        List<AssetAcknowledgementItemPayload> OutstandingAssetAcknowledgements,
        ProbationSummaryPayload? Probation);

    private sealed record OnboardingTaskItemPayload(
        Guid Id,
        string Title,
        string Status,
        DateOnly? DueDate,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset UpdatedAt);

    private sealed record DocumentRequestItemPayload(Guid Id, string DocumentTypeName, DateOnly? DueDate, bool IsMandatory);

    private sealed record AssetAcknowledgementItemPayload(Guid AssetAssignmentId, Guid AssetId, string AssetLabel, DateTimeOffset AssignedAt);

    private sealed record ProbationSummaryPayload(string Status, DateOnly StartDate, DateOnly ExpectedEndDate, DateOnly? DecisionDate);
}
