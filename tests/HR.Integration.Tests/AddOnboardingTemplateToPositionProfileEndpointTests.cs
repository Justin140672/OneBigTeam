using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class AddOnboardingTemplateToPositionProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000015-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdministratorUserId = new("cc000015-0000-0000-0000-000000000002");

    public AddOnboardingTemplateToPositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUserId, SystemRoles.CompanyAdministrator);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private HttpClient CompanyAdministratorClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, CompanyAdministratorUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}/onboarding-templates",
            new { onboardingTemplateId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_Forbidden_For_User_Without_Employee_Manage_Permission()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = AdminClient(companyId);
        var profileId = await CreatePositionProfileAsync(adminClient, companyId, "Software Engineer");
        var templateId = await CreateOnboardingTemplateAsync(adminClient, companyId, "Standard Onboarding");

        using var client = CompanyAdministratorClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = templateId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_Created_With_Correct_Payload()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Software Engineer");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new
            {
                companyId,
                positionProfileId = profileId,
                onboardingTemplateId = templateId
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<OnboardingTemplateAssignmentPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(profileId, payload.PositionProfileId);
        Assert.Equal(templateId, payload.OnboardingTemplateId);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_Conflict_For_Duplicate_Assignment()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "HR Manager");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding");

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = templateId });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = templateId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_NotFound_For_Unknown_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}/onboarding-templates",
            new { companyId, positionProfileId = Guid.NewGuid(), onboardingTemplateId = templateId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_NotFound_For_Unknown_OnboardingTemplate()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Finance Manager");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_NotFound_For_Inactive_OnboardingTemplate()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Operations Lead");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Retired Onboarding");

        var deactivateResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/onboarding-templates/{templateId}");
        deactivateResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = templateId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Allows_Same_Template_On_Different_Profile()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var profileAId = await CreatePositionProfileAsync(client, companyId, "Developer");
        var profileBId = await CreatePositionProfileAsync(client, companyId, "Designer");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding");

        var responseA = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileAId}/onboarding-templates",
            new { companyId, positionProfileId = profileAId, onboardingTemplateId = templateId });
        responseA.EnsureSuccessStatusCode();

        var responseB = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileBId}/onboarding-templates",
            new { companyId, positionProfileId = profileBId, onboardingTemplateId = templateId });

        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_UnprocessableEntity_For_Empty_OnboardingTemplateId()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Recruiter");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = Guid.Empty });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task<Guid> CreatePositionProfileAsync(HttpClient client, Guid companyId, string title)
    {
        var departmentId = await CreateDepartmentAsync(client, companyId);
        var locationId = await CreateLocationAsync(client, companyId);
        var leavePolicyId = await CreateLeavePolicyAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, defaultLeavePolicyId = leavePolicyId, title });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
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

    private async Task<Guid> CreateOnboardingTemplateAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates",
            new { companyId, name });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OnboardingTemplateAssignmentPayload(
        Guid Id,
        Guid PositionProfileId,
        Guid OnboardingTemplateId);
}
