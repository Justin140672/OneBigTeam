using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DeactivateOnboardingTemplateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000014-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdministratorUserId = new("cc000014-0000-0000-0000-000000000002");

    public DeactivateOnboardingTemplateEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee);
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

    private static async Task<OnboardingTemplatePayload> CreateTemplateAsync(HttpClient client, Guid companyId, string name = "Standard Onboarding")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OnboardingTemplatePayload>())!;
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/onboarding-templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_Forbidden_For_User_Without_Employee_Manage_Permission()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = AdminClient(companyId);
        var template = await CreateTemplateAsync(adminClient, companyId);

        using var client = CompanyAdministratorClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_NotFound_When_Template_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/onboarding-templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_NoContent_On_Success()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Deactivates_The_Template()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId);

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/onboarding-templates");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<ListOnboardingTemplatesPayload>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list!.Items, t => t.Id == template.Id);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_NotFound_When_Already_Deactivated()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var template = await CreateTemplateAsync(client, companyId);

        var firstDelete = await client.DeleteAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}");
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);

        var secondDelete = await client.DeleteAsync(
            $"/api/companies/{companyId}/onboarding-templates/{template.Id}");

        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task Delete_OnboardingTemplate_Returns_NotFound_When_Template_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var createClient = AdminClient(companyId);

        var template = await CreateTemplateAsync(createClient, companyId, "Vehicles Onboarding");

        using var otherClient = AdminClient(otherCompanyId);
        var response = await otherClient.DeleteAsync(
            $"/api/companies/{otherCompanyId}/onboarding-templates/{template.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record OnboardingTemplatePayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt);

    private sealed record ListOnboardingTemplatesPayload(List<OnboardingTemplateListItemPayload> Items);

    private sealed record OnboardingTemplateListItemPayload(
        Guid Id,
        string Name,
        string? Description,
        bool IsActive,
        int TaskCount);
}
