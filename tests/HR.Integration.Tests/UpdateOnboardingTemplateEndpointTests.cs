using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class UpdateOnboardingTemplateEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000011-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdministratorUserId = new("cc000011-0000-0000-0000-000000000002");

    public UpdateOnboardingTemplateEndpointTests(ApiWebApplicationFactory factory)
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

    private static async Task<OnboardingTemplatePayload> CreateTemplateAsync(
        HttpClient client, Guid companyId, string name = "Standard Onboarding", string? description = null)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name,
            description
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OnboardingTemplatePayload>())!;
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/onboarding-templates/{Guid.NewGuid()}",
            new { name = "Standard Onboarding" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Returns_Forbidden_For_User_Without_Employee_Manage_Permission()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = AdminClient(companyId);
        var template = await CreateTemplateAsync(adminClient, companyId);

        using var client = CompanyAdministratorClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}",
            new { companyId, id = template.Id, name = "Updated Name", tasks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Returns_NotFound_When_Template_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{Guid.NewGuid()}",
            new { companyId, id = Guid.NewGuid(), name = "Standard Onboarding", tasks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Updates_Name_Description_And_Tasks()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId, "Standard Onboarding");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}",
            new
            {
                companyId,
                id = template.Id,
                name = "Engineering Onboarding",
                description = "Onboarding checklist for engineering new hires",
                tasks = new object[]
                {
                    new
                    {
                        id = (Guid?)null,
                        title = "Set up laptop",
                        description = "IT provisions a laptop before day one",
                        priority = "High",
                        assignTo = "Manager",
                        dueDaysAfterStart = 0,
                        displayOrder = 1
                    },
                    new
                    {
                        id = (Guid?)null,
                        title = "Complete tax forms",
                        description = (string?)null,
                        priority = "Medium",
                        assignTo = "NewHire",
                        dueDaysAfterStart = 3,
                        displayOrder = 2
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<OnboardingTemplateDetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal(template.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Engineering Onboarding", payload.Name);
        Assert.Equal("Onboarding checklist for engineering new hires", payload.Description);
        Assert.True(payload.IsActive);
        Assert.Equal(2, payload.Tasks.Count);

        var firstTask = payload.Tasks[0];
        Assert.Equal("Set up laptop", firstTask.Title);
        Assert.Equal("IT provisions a laptop before day one", firstTask.Description);
        Assert.Equal("High", firstTask.Priority);
        Assert.Equal("Manager", firstTask.AssignTo);
        Assert.Equal(0, firstTask.DueDaysAfterStart);
        Assert.Equal(1, firstTask.DisplayOrder);

        var secondTask = payload.Tasks[1];
        Assert.Equal("Complete tax forms", secondTask.Title);
        Assert.Null(secondTask.Description);
        Assert.Equal("Medium", secondTask.Priority);
        Assert.Equal("NewHire", secondTask.AssignTo);
        Assert.Equal(3, secondTask.DueDaysAfterStart);
        Assert.Equal(2, secondTask.DisplayOrder);
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Replaces_Existing_Tasks_On_Subsequent_Update()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId, "Standard Onboarding");

        var firstUpdate = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}",
            new
            {
                companyId,
                id = template.Id,
                name = "Standard Onboarding",
                tasks = new object[]
                {
                    new
                    {
                        id = (Guid?)null,
                        title = "Original task",
                        description = (string?)null,
                        priority = "Low",
                        assignTo = "Unassigned",
                        dueDaysAfterStart = 1,
                        displayOrder = 1
                    }
                }
            });
        firstUpdate.EnsureSuccessStatusCode();

        var secondUpdate = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}",
            new
            {
                companyId,
                id = template.Id,
                name = "Standard Onboarding",
                tasks = new object[]
                {
                    new
                    {
                        id = (Guid?)null,
                        title = "Replacement task",
                        description = (string?)null,
                        priority = "Critical",
                        assignTo = "Manager",
                        dueDaysAfterStart = 5,
                        displayOrder = 1
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, secondUpdate.StatusCode);
        var payload = await secondUpdate.Content.ReadFromJsonAsync<OnboardingTemplateDetailPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Tasks);
        Assert.Equal("Replacement task", payload.Tasks[0].Title);
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Returns_Conflict_When_Renamed_To_Existing_Active_Template_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        await CreateTemplateAsync(client, companyId, "Standard Onboarding");
        var second = await CreateTemplateAsync(client, companyId, "Executive Onboarding");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{second.Id}",
            new { companyId, id = second.Id, name = "Standard Onboarding", tasks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Allows_Keeping_Its_Own_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId, "Standard Onboarding");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}",
            new
            {
                companyId,
                id = template.Id,
                name = "Standard Onboarding",
                description = "Updated description only",
                tasks = Array.Empty<object>()
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OnboardingTemplateDetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Standard Onboarding", payload!.Name);
        Assert.Equal("Updated description only", payload.Description);
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Returns_UnprocessableEntity_When_Name_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId, "Standard Onboarding");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}",
            new { companyId, id = template.Id, name = string.Empty, tasks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_OnboardingTemplate_Returns_UnprocessableEntity_When_Task_Title_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId, "Standard Onboarding");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}",
            new
            {
                companyId,
                id = template.Id,
                name = "Standard Onboarding",
                tasks = new object[]
                {
                    new
                    {
                        id = (Guid?)null,
                        title = string.Empty,
                        description = (string?)null,
                        priority = "Low",
                        assignTo = "Unassigned",
                        dueDaysAfterStart = 1,
                        displayOrder = 1
                    }
                }
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record OnboardingTemplatePayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt);

    private sealed record OnboardingTemplateDetailPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset UpdatedAt,
        List<OnboardingTemplateTaskPayload> Tasks);

    private sealed record OnboardingTemplateTaskPayload(
        Guid Id,
        string Title,
        string? Description,
        string Priority,
        string AssignTo,
        int DueDaysAfterStart,
        int DisplayOrder);
}
