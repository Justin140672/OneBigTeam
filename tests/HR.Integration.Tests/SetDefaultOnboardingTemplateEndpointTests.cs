using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SetDefaultOnboardingTemplateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc0000f1-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdministratorUserId = new("cc0000f1-0000-0000-0000-000000000002");

    public SetDefaultOnboardingTemplateEndpointTests(ApiWebApplicationFactory factory)
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

    private static async Task<TemplatePayload> CreateTemplateAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates", new
        {
            companyId,
            name
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TemplatePayload>())!;
    }

    private static Task<HttpResponseMessage> SetDefaultAsync(HttpClient client, Guid companyId, Guid id) =>
        client.PostAsJsonAsync($"/api/companies/{companyId}/onboarding-templates/{id}/set-default", new { });

    private static async Task<List<ListItemPayload>> ListAsync(HttpClient client, Guid companyId, bool includeInactive = false)
    {
        var url = $"/api/companies/{companyId}/onboarding-templates" + (includeInactive ? "?includeInactive=true" : "");
        var payload = await client.GetFromJsonAsync<ListPayload>(url);
        return payload!.Items;
    }

    [Fact]
    public async Task SetDefault_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await SetDefaultAsync(client, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetDefault_Returns_Forbidden_For_User_Without_Employee_Manage_Permission()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await AdminClient(companyId);
        var template = await CreateTemplateAsync(adminClient, companyId, "Only Onboarding");

        using var client = await CompanyAdministratorClient(companyId);

        var response = await SetDefaultAsync(client, companyId, template.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetDefault_Returns_NotFound_When_Template_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await SetDefaultAsync(client, companyId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetDefault_Returns_NotFound_When_Template_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var createClient = await AdminClient(companyId);
        var template = await CreateTemplateAsync(createClient, companyId, "Vehicles Onboarding");

        using var otherClient = await AdminClient(otherCompanyId);
        var response = await SetDefaultAsync(otherClient, otherCompanyId, template.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetDefault_Returns_BadRequest_When_Template_Is_Inactive()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        await CreateTemplateAsync(client, companyId, "Default Onboarding");
        var inactive = await CreateTemplateAsync(client, companyId, "Inactive Onboarding");
        (await client.DeleteAsync($"/api/companies/{companyId}/onboarding-templates/{inactive.Id}")).EnsureSuccessStatusCode();

        var response = await SetDefaultAsync(client, companyId, inactive.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetDefault_Switches_The_Default_Template()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var first = await CreateTemplateAsync(client, companyId, "First Onboarding");
        var second = await CreateTemplateAsync(client, companyId, "Second Onboarding");

        var response = await SetDefaultAsync(client, companyId, second.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var items = await ListAsync(client, companyId);
        Assert.False(items.Single(i => i.Id == first.Id).IsDefault);
        Assert.True(items.Single(i => i.Id == second.Id).IsDefault);
        Assert.Single(items, i => i.IsDefault);
    }

    [Fact]
    public async Task SetDefault_Is_Idempotent_When_Template_Is_Already_Default()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var first = await CreateTemplateAsync(client, companyId, "First Onboarding");

        var response = await SetDefaultAsync(client, companyId, first.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True((await ListAsync(client, companyId)).Single(i => i.Id == first.Id).IsDefault);
    }

    [Fact]
    public async Task SetDefault_Then_Deactivating_Previous_Default_Succeeds()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var first = await CreateTemplateAsync(client, companyId, "First Onboarding");
        var second = await CreateTemplateAsync(client, companyId, "Second Onboarding");

        (await SetDefaultAsync(client, companyId, second.Id)).EnsureSuccessStatusCode();

        var deleteResponse = await client.DeleteAsync($"/api/companies/{companyId}/onboarding-templates/{first.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    private sealed record TemplatePayload(Guid Id, string Name, bool IsActive, bool IsDefault);

    private sealed record ListPayload(List<ListItemPayload> Items);

    private sealed record ListItemPayload(Guid Id, string Name, bool IsActive, bool IsDefault);
}
