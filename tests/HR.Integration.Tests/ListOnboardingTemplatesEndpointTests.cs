using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListOnboardingTemplatesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000013-0000-0000-0000-000000000001");

    public ListOnboardingTemplatesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static async Task<OnboardingTemplatePayload> CreateTemplateAsync(HttpClient client, Guid companyId, string name)
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
    public async Task Get_OnboardingTemplates_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/onboarding-templates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// ListOnboardingTemplatesHandler lazily seeds a default "Standard Onboarding" template for
    /// any company that has never had one (OnboardingTemplateSeeder.EnsureDefaultTemplateSeededAsync,
    /// called unconditionally at the top of the handler) — so a company that has never created its
    /// own template still isn't truly empty; it always has exactly this one.
    /// </summary>
    [Fact]
    public async Task Get_OnboardingTemplates_Returns_Only_The_Seeded_Default_When_No_Templates_Created()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/onboarding-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListOnboardingTemplatesPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal("Standard Onboarding", item.Name);
        Assert.True(item.IsActive);
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Returns_Active_Templates_Ordered_By_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        await CreateTemplateAsync(client, companyId, "Remote Onboarding");
        await CreateTemplateAsync(client, companyId, "Executive Onboarding");
        await CreateTemplateAsync(client, companyId, "Standard Onboarding");

        var response = await client.GetAsync($"/api/companies/{companyId}/onboarding-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListOnboardingTemplatesPayload>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Items.Count);
        Assert.Equal("Executive Onboarding", payload.Items[0].Name);
        Assert.Equal("Remote Onboarding", payload.Items[1].Name);
        Assert.Equal("Standard Onboarding", payload.Items[2].Name);
        Assert.All(payload.Items, i => Assert.True(i.IsActive));
        Assert.All(payload.Items, i => Assert.Equal(0, i.TaskCount));
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Excludes_Inactive_Templates_By_Default()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var active = await CreateTemplateAsync(client, companyId, "Active Onboarding");
        var toDeactivate = await CreateTemplateAsync(client, companyId, "Inactive Onboarding");

        var deleteResponse = await client.DeleteAsync($"/api/companies/{companyId}/onboarding-templates/{toDeactivate.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/onboarding-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListOnboardingTemplatesPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(active.Id, payload.Items[0].Id);
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Includes_Inactive_Templates_When_Requested()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var active = await CreateTemplateAsync(client, companyId, "Active Onboarding");
        var toDeactivate = await CreateTemplateAsync(client, companyId, "Inactive Onboarding");

        var deleteResponse = await client.DeleteAsync($"/api/companies/{companyId}/onboarding-templates/{toDeactivate.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/onboarding-templates?includeInactive=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListOnboardingTemplatesPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == active.Id && i.IsActive);
        Assert.Contains(payload.Items, i => i.Id == toDeactivate.Id && !i.IsActive);
    }

    [Fact]
    public async Task Get_OnboardingTemplates_Does_Not_Return_Templates_From_Other_Companies()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        using var otherClient = AdminClient(otherCompanyId);

        await otherClient.PostAsJsonAsync($"/api/companies/{otherCompanyId}/onboarding-templates", new
        {
            companyId = otherCompanyId,
            name = "Other Company Onboarding"
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/onboarding-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListOnboardingTemplatesPayload>();
        Assert.NotNull(payload);
        // Only this company's own lazily-seeded default — never the other company's
        // manually-created "Other Company Onboarding" template.
        var item = Assert.Single(payload!.Items);
        Assert.Equal("Standard Onboarding", item.Name);
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
