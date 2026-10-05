using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SuggestWorkEmailEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("7e0a1003-0000-0000-0000-000000000001");
    private static readonly Guid EmployeeOnlyUserId = new("7e0a1003-0000-0000-0000-000000000002");

    public SuggestWorkEmailEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeOnlyUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, tenantId);
        return client;
    }

    private static string Url(Guid companyId, string? firstName = "Jane", string? lastName = "Smith", string? domain = null)
    {
        var url = $"/api/companies/{companyId}/employees/work-email-suggestion" +
                  $"?firstName={Uri.EscapeDataString(firstName ?? string.Empty)}" +
                  $"&lastName={Uri.EscapeDataString(lastName ?? string.Empty)}";
        return domain is null ? url : $"{url}&domain={Uri.EscapeDataString(domain)}";
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Returns_Forbidden_For_Non_Manager()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(EmployeeOnlyUserId, tenantId);

        var response = await client.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Returns_NotConfigured_When_Company_Has_No_Primary_Domain()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var payload = await client.GetFromJsonAsync<SuggestionPayload>(Url(tenantId));

        Assert.Equal("NotConfigured", payload!.Status);
        Assert.Null(payload.Suggestion);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Returns_Disabled_When_Suggestions_Are_Switched_Off()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        await WorkEmailTestHelper.ConfigureAsync(client, tenantId, enabled: false, primaryDomain: "example.com");

        var payload = await client.GetFromJsonAsync<SuggestionPayload>(Url(tenantId));

        Assert.Equal("Disabled", payload!.Status);
        Assert.Null(payload.Suggestion);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Returns_Available_Suggestion()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        await WorkEmailTestHelper.ConfigureAsync(client, tenantId, primaryDomain: "example.com");

        var payload = await client.GetFromJsonAsync<SuggestionPayload>(Url(tenantId));

        Assert.Equal("Available", payload!.Status);
        Assert.Equal("jane.smith@example.com", payload.Suggestion);
        Assert.Equal("example.com", payload.SelectedDomain);
        Assert.Equal(["example.com"], payload.Domains);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Uses_Selected_Additional_Domain_And_Convention()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        await WorkEmailTestHelper.ConfigureAsync(
            client, tenantId, primaryDomain: "example.com", additionalDomains: ["alt.example.com"], convention: "FirstInitialDotLastName");

        var payload = await client.GetFromJsonAsync<SuggestionPayload>(Url(tenantId, domain: "alt.example.com"));

        Assert.Equal("Available", payload!.Status);
        Assert.Equal("j.smith@alt.example.com", payload.Suggestion);
        Assert.Equal(["example.com", "alt.example.com"], payload.Domains);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Falls_Back_To_Primary_For_Unknown_Domain()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        await WorkEmailTestHelper.ConfigureAsync(client, tenantId, primaryDomain: "example.com");

        var payload = await client.GetFromJsonAsync<SuggestionPayload>(Url(tenantId, domain: "unknown.com"));

        Assert.Equal("jane.smith@example.com", payload!.Suggestion);
        Assert.Equal("example.com", payload.SelectedDomain);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Returns_NameIncomplete_When_Last_Name_Is_Missing()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        await WorkEmailTestHelper.ConfigureAsync(client, tenantId);

        var payload = await client.GetFromJsonAsync<SuggestionPayload>(Url(tenantId, lastName: ""));

        Assert.Equal("NameIncomplete", payload!.Status);
        Assert.Null(payload.Suggestion);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Returns_Unavailable_When_Another_Employee_In_Company_Has_The_Address()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        await WorkEmailTestHelper.ConfigureAsync(client, tenantId, primaryDomain: "example.com");
        await WorkEmailTestHelper.AddEmployeeAsync(_factory, tenantId, "jane.smith@example.com");

        var payload = await client.GetFromJsonAsync<SuggestionPayload>(Url(tenantId));

        Assert.Equal("Unavailable", payload!.Status);
        Assert.Equal("jane.smith@example.com", payload.Suggestion);
    }

    [Fact]
    public async Task Get_WorkEmailSuggestion_Is_Not_Unavailable_When_Another_Company_Has_The_Same_Address()
    {
        var tenantId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        await WorkEmailTestHelper.ConfigureAsync(client, tenantId, primaryDomain: "example.com");
        await WorkEmailTestHelper.AddEmployeeAsync(_factory, otherCompanyId, "jane.smith@example.com");

        var payload = await client.GetFromJsonAsync<SuggestionPayload>(Url(tenantId));

        Assert.Equal("Available", payload!.Status);
        Assert.Equal("jane.smith@example.com", payload.Suggestion);
    }

    private sealed record SuggestionPayload(string Status, string? Suggestion, string? SelectedDomain, string[] Domains);
}
