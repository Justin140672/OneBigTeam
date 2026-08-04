using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DismissOnboardingChecklistEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("cc000002-0000-0000-0000-000000000001");
    private static readonly Guid EmployeeUserId = new("cc000002-0000-0000-0000-000000000002");

    public DismissOnboardingChecklistEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    [Fact]
    public async Task Post_Dismiss_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/company-onboarding/checklist/dismiss", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Dismiss_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, EmployeeUserId);

        var response = await client.PostAsync("/api/company-onboarding/checklist/dismiss", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Dismiss_Returns_Ok_And_Sets_IsHidden()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUserId);

        var response = await client.PostAsync("/api/company-onboarding/checklist/dismiss", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DismissPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.IsHidden);
    }

    [Fact]
    public async Task Post_Dismiss_Then_Get_Checklist_Reflects_IsHidden_True()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUserId);

        var dismissResponse = await client.PostAsync("/api/company-onboarding/checklist/dismiss", content: null);
        Assert.Equal(HttpStatusCode.OK, dismissResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/company-onboarding/checklist");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var payload = await getResponse.Content.ReadFromJsonAsync<ChecklistPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.IsHidden);
        Assert.True(payload.IsDismissedEarly);
    }

    private sealed record DismissPayload(bool IsHidden);

    private sealed record ChecklistPayload(
        List<object> Tasks,
        int CompletionPercentage,
        bool IsHidden,
        bool IsDismissedEarly);
}
