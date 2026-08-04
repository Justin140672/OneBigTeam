using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateOnboardingTemplateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000010-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdministratorUserId = new("cc000010-0000-0000-0000-000000000002");

    public CreateOnboardingTemplateEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
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
    public async Task Post_OnboardingTemplates_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/onboarding-templates",
            new { name = "Standard Onboarding" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_Forbidden_For_User_Without_Employee_Manage_Permission()
    {
        var companyId = Guid.NewGuid();
        using var client = await CompanyAdministratorClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name = "Standard Onboarding"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Creates_OnboardingTemplate()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name = "Standard Onboarding",
            description = "Default onboarding checklist for new hires"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<OnboardingTemplatePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Standard Onboarding", payload.Name);
        Assert.Equal("Default onboarding checklist for new hires", payload.Description);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Creates_OnboardingTemplate_Without_Description()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name = "Remote Onboarding"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<OnboardingTemplatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Remote Onboarding", payload!.Name);
        Assert.Null(payload.Description);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_Conflict_When_Active_Template_With_Same_Name_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name = "Standard Onboarding"
        });
        created.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name = "Standard Onboarding"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Allows_Same_Name_In_Different_Companies()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        using var otherClient = await AdminClient(otherCompanyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name = "Standard Onboarding"
        });
        created.EnsureSuccessStatusCode();

        var response = await otherClient.PostAsJsonAsync($"/api/companies/{otherCompanyId}/onboarding-templates", new
        {
            companyId = otherCompanyId,
            name = "Standard Onboarding"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_OnboardingTemplates_Returns_UnprocessableEntity_When_Name_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name = string.Empty
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
}
