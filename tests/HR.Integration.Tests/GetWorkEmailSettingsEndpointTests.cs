using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetWorkEmailSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("7e0a1002-0000-0000-0000-000000000001");
    private static readonly Guid EmployeeOnlyUserId = new("7e0a1002-0000-0000-0000-000000000002");

    public GetWorkEmailSettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeOnlyUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid tenantId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, tenantId, ensureActiveSubscription);
        return client;
    }

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/work-email-settings";

    [Fact]
    public async Task Get_WorkEmailSettings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WorkEmailSettings_Returns_Forbidden_For_Non_HrAdministrator()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(EmployeeOnlyUserId, tenantId);

        var response = await client.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_WorkEmailSettings_Returns_Defaults_And_Examples_For_HrAdministrator()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(tenantId, payload!.CompanyId);
        Assert.True(payload.SuggestionsEnabled);
        Assert.Null(payload.PrimaryDomain);
        Assert.Equal("FirstNameDotLastName", payload.NamingConvention);
        Assert.Equal("Jane", payload.ExampleFirstName);
        Assert.Equal("Smith", payload.ExampleLastName);
        Assert.Equal(4, payload.Examples.Length);
        Assert.Equal("jane.smith", payload.Examples.Single(e => e.Convention == "FirstNameDotLastName").LocalPart);
        Assert.Equal("j.smith", payload.Examples.Single(e => e.Convention == "FirstInitialDotLastName").LocalPart);
        Assert.Equal("janesmith", payload.Examples.Single(e => e.Convention == "FirstNameLastName").LocalPart);
        Assert.Equal("jane", payload.Examples.Single(e => e.Convention == "FirstName").LocalPart);
    }

    [Fact]
    public async Task Get_WorkEmailSettings_Returns_Saved_Values()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        await WorkEmailTestHelper.ConfigureAsync(
            client, tenantId, primaryDomain: "example.com", convention: "FirstNameLastName");

        var payload = await client.GetFromJsonAsync<SettingsPayload>(Url(tenantId));

        Assert.True(payload!.SuggestionsEnabled);
        Assert.Equal("example.com", payload.PrimaryDomain);
        Assert.Equal("FirstNameLastName", payload.NamingConvention);
        Assert.Equal(2, payload.Version);
    }

    [Fact]
    public async Task Get_WorkEmailSettings_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId, ensureActiveSubscription: false);

        var response = await client.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ExamplePayload(string Convention, string LocalPart);

    private sealed record SettingsPayload(
        Guid CompanyId,
        bool SuggestionsEnabled,
        string? PrimaryDomain,
        string NamingConvention,
        string ExampleFirstName,
        string ExampleLastName,
        ExamplePayload[] Examples,
        int Version);
}
