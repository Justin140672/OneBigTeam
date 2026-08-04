using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetOnboardingChecklistEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("cc000001-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdminUserId = new("cc000001-0000-0000-0000-000000000002");
    private static readonly Guid EmployeeUserId = new("cc000001-0000-0000-0000-000000000003");

    public GetOnboardingChecklistEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_Checklist_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/company-onboarding/checklist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Checklist_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(companyId, EmployeeUserId);

        var response = await client.GetAsync("/api/company-onboarding/checklist");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Checklist_Returns_Ok_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(companyId, HrAdminUserId);

        var response = await client.GetAsync("/api/company-onboarding/checklist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ChecklistPayload>();
        Assert.NotNull(payload);
        Assert.Equal(7, payload!.Tasks.Count);
        Assert.InRange(payload.CompletionPercentage, 0, 100);
    }

    [Fact]
    public async Task Get_Checklist_Returns_Ok_For_CompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(companyId, CompanyAdminUserId);

        var response = await client.GetAsync("/api/company-onboarding/checklist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ChecklistPayload>();
        Assert.NotNull(payload);
        Assert.Equal(7, payload!.Tasks.Count);
    }

    private sealed record OnboardingTaskItemPayload(
        string Key,
        string Name,
        string Description,
        bool IsMandatory,
        string LinkUrl,
        int Order,
        bool IsCompleted,
        DateTimeOffset? CompletedAt);

    private sealed record ChecklistPayload(
        List<OnboardingTaskItemPayload> Tasks,
        int CompletionPercentage,
        bool IsHidden,
        bool IsDismissedEarly);
}
