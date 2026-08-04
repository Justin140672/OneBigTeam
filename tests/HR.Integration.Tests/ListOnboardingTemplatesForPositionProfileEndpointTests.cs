using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListOnboardingTemplatesForPositionProfileEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("cc000016-0000-0000-0000-000000000001");

    public ListOnboardingTemplatesForPositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}/onboarding-templates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Returns_NotFound_For_Unknown_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}/onboarding-templates");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Returns_Empty_List_When_None_Assigned()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Operations Lead");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Returns_Assigned_Templates_With_TaskCount()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Software Engineer");
        var templateId = await CreateOnboardingTemplateAsync(client, companyId, "Standard Onboarding", "Default checklist");
        await SetTemplateTasksAsync(client, companyId, templateId, "Default checklist", "Send welcome email", "Order equipment");

        await AssignOnboardingTemplateAsync(client, companyId, profileId, templateId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        var item = payload.Items[0];
        Assert.Equal(templateId, item.OnboardingTemplateId);
        Assert.Equal("Standard Onboarding", item.Name);
        Assert.Equal("Default checklist", item.Description);
        Assert.Equal(2, item.TaskCount);
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Excludes_Removed_Assignments()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "HR Manager");
        var templateAId = await CreateOnboardingTemplateAsync(client, companyId, "Template A", null);
        var templateBId = await CreateOnboardingTemplateAsync(client, companyId, "Template B", null);

        var assignmentAId = await AssignOnboardingTemplateAsync(client, companyId, profileId, templateAId);
        await AssignOnboardingTemplateAsync(client, companyId, profileId, templateBId);

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates/{assignmentAId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(templateBId, payload.Items[0].OnboardingTemplateId);
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Is_Scoped_To_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await AuthenticatedClient(companyA);
        using var clientB = await AuthenticatedClient(companyB);

        var profileAId = await CreatePositionProfileAsync(clientA, companyA, "Developer");
        var profileBId = await CreatePositionProfileAsync(clientB, companyB, "Developer");

        var templateAId = await CreateOnboardingTemplateAsync(clientA, companyA, "Template A", null);
        var templateBId = await CreateOnboardingTemplateAsync(clientB, companyB, "Template B", null);

        await AssignOnboardingTemplateAsync(clientA, companyA, profileAId, templateAId);
        await AssignOnboardingTemplateAsync(clientB, companyB, profileBId, templateBId);

        var response = await clientA.GetAsync(
            $"/api/companies/{companyA}/position-profiles/{profileAId}/onboarding-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(templateAId, payload.Items[0].OnboardingTemplateId);
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
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
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

    private async Task<Guid> CreateOnboardingTemplateAsync(HttpClient client, Guid companyId, string name, string? description)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates",
            new { companyId, name, description });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task SetTemplateTasksAsync(
        HttpClient client, Guid companyId, Guid templateId, string? description, params string[] taskTitles)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{templateId}",
            new
            {
                companyId,
                id = templateId,
                name = "Standard Onboarding",
                description,
                tasks = taskTitles.Select((title, index) => new
                {
                    id = (Guid?)null,
                    title,
                    description = (string?)null,
                    priority = "Medium",
                    assignTo = "NewHire",
                    dueDaysAfterStart = 1,
                    displayOrder = index + 1
                }).ToArray()
            });
        response.EnsureSuccessStatusCode();
    }

    private async Task<Guid> AssignOnboardingTemplateAsync(
        HttpClient client, Guid companyId, Guid profileId, Guid templateId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/onboarding-templates",
            new { companyId, positionProfileId = profileId, onboardingTemplateId = templateId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
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
