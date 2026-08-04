using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetOnboardingTemplateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000012-0000-0000-0000-000000000001");

    public GetOnboardingTemplateEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee))
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

    private static async Task<HttpResponseMessage> UpdateWithTaskAsync(HttpClient client, Guid companyId, OnboardingTemplatePayload template)
    {
        return await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}",
            new
            {
                companyId,
                id = template.Id,
                name = template.Name,
                description = template.Description,
                tasks = new object[]
                {
                    new
                    {
                        id = (Guid?)null,
                        title = "Collect signed contract",
                        description = "Ensure the employment contract is signed before start date",
                        priority = "High",
                        assignTo = "NewHire",
                        dueDaysAfterStart = 0,
                        displayOrder = 1
                    }
                }
            });
    }

    [Fact]
    public async Task Get_OnboardingTemplate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/onboarding-templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OnboardingTemplate_Returns_NotFound_When_Template_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/onboarding-templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_OnboardingTemplate_Returns_Template_With_Tasks_When_Found()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId, "Standard Onboarding", "Default checklist");

        var updateResponse = await UpdateWithTaskAsync(client, companyId, template);
        updateResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/onboarding-templates/{template.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<OnboardingTemplateDetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal(template.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Standard Onboarding", payload.Name);
        Assert.Equal("Default checklist", payload.Description);
        Assert.True(payload.IsActive);
        Assert.Single(payload.Tasks);
        Assert.Equal("Collect signed contract", payload.Tasks[0].Title);
        Assert.Equal("High", payload.Tasks[0].Priority);
        Assert.Equal("NewHire", payload.Tasks[0].AssignTo);
    }

    [Fact]
    public async Task Get_OnboardingTemplate_Returns_NotFound_When_Template_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        using var otherClient = await AdminClient(otherCompanyId);

        var template = await CreateTemplateAsync(client, companyId);

        var response = await otherClient.GetAsync($"/api/companies/{otherCompanyId}/onboarding-templates/{template.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        DateTimeOffset CreatedAt,
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
