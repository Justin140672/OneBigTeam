using System.Net;
using System.Net.Http.Json;
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
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
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

        var employeeId = await CreateEmployeeAsync(client, companyId, positionProfileId: null);

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
        var docTypeId = await CreateDocumentTypeAsync(client, companyId, "Passport");
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
        Assert.Equal("Passport", docRequest.DocumentTypeName);
        Assert.True(docRequest.IsMandatory);

        var assetAck = Assert.Single(payload.OutstandingAssetAcknowledgements);
        Assert.Equal(assetId, assetAck.AssetId);

        Assert.NotNull(payload.Probation);
        Assert.Equal("Active", payload.Probation!.Status);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId, Guid? positionProfileId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            BuildEmployeeBody(companyId, "Onboard", $"Employee{Guid.NewGuid():N}", positionProfileId));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static object BuildEmployeeBody(Guid companyId, string firstName, string lastName, Guid? positionProfileId) =>
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
            positionProfileId,
        };

    private async Task<Guid> CreatePositionProfileAsync(HttpClient client, Guid companyId, string title)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, title });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<Guid> CreateDocumentTypeAsync(HttpClient client, Guid companyId, string name)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { companyId, name = $"{name} {Guid.NewGuid():N}" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
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

    private sealed record IdPayload(Guid Id);

    private sealed record OverviewPayload(
        Guid EmployeeId,
        bool HasPlan,
        string? PlanStatus,
        DateOnly? StartDate,
        List<OnboardingTaskItemPayload> Tasks,
        List<DocumentRequestItemPayload> OutstandingDocumentRequests,
        List<AssetAcknowledgementItemPayload> OutstandingAssetAcknowledgements,
        ProbationSummaryPayload? Probation);

    private sealed record OnboardingTaskItemPayload(Guid Id, string Title, string Status, DateOnly? DueDate);

    private sealed record DocumentRequestItemPayload(Guid Id, string DocumentTypeName, DateOnly? DueDate, bool IsMandatory);

    private sealed record AssetAcknowledgementItemPayload(Guid AssetAssignmentId, Guid AssetId, string AssetLabel, DateTimeOffset AssignedAt);

    private sealed record ProbationSummaryPayload(string Status, DateOnly StartDate, DateOnly ExpectedEndDate, DateOnly? DecisionDate);
}
