using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RemoveOnboardingTemplateFromPositionProfileEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000017-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdministratorUserId = new("cc000017-0000-0000-0000-000000000002");

    public RemoveOnboardingTemplateFromPositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUserId, SystemRoles.CompanyAdministrator);
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

    private async Task<HttpClient> CompanyAdministratorClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, CompanyAdministratorUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, CompanyAdministratorUserId, SystemRoles.CompanyAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}/onboarding-templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_Forbidden_For_User_Without_Employee_Manage_Permission()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await AdminClient(companyId);
        var profileId = await CreatePositionProfileAsync(adminClient, companyId, "Software Engineer");
        var templateId = await CreateOnboardingTemplateAsync(adminClient, companyId, "Standard Onboarding");
        var assignmentId = await AssignOnboardingTemplateAsync(adminClient, companyId, profileId, templateId);

        using var client = await CompanyAdministratorClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates/{assignmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_NoContent_And_Deactivates_Assignment()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Software Engineer");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding");
        var assignmentId = await AssignOnboardingTemplateAsync(client, companyId, profileId, templateId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates/{assignmentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var listResponse = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates");
        listResponse.EnsureSuccessStatusCode();
        var payload = await listResponse.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_NotFound_When_Already_Removed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "HR Manager");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding");
        var assignmentId = await AssignOnboardingTemplateAsync(client, companyId, profileId, templateId);

        var first = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates/{assignmentId}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates/{assignmentId}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_NotFound_For_Unknown_Assignment()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Finance Manager");

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_NotFound_When_Assignment_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Operations Lead");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding");
        var assignmentId = await AssignOnboardingTemplateAsync(client, companyId, profileId, templateId);

        using var otherClient = await AdminClient(otherCompanyId);
        var response = await otherClient.DeleteAsync(
            $"/api/companies/{otherCompanyId}/position-profiles/{profileId}/onboarding-templates/{assignmentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Allows_Same_Template_To_Be_Re_Assigned_After_Removal()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Recruiter");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding");
        var assignmentId = await AssignOnboardingTemplateAsync(client, companyId, profileId, templateId);

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates/{assignmentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var reAssignResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = templateId });

        Assert.Equal(HttpStatusCode.Created, reAssignResponse.StatusCode);
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

    private async Task<Guid> AssignOnboardingTemplateAsync(
        HttpClient client, Guid companyId, Guid profileId, Guid templateId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = templateId });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OnboardingTemplateAssignmentItem(
        Guid Id,
        Guid OnboardingTemplateId,
        string Name,
        string? Description,
        int TaskCount);

    private sealed record ListPayload(IReadOnlyList<OnboardingTemplateAssignmentItem> Items);
}
