using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetOnboardingChecklistEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    // Guid.NewGuid() rather than hardcoded literals — under the shared-database "Integration"
    // collection, a fixed literal here previously collided with the same literal used (and
    // assigned a different role) in CompanyAuthorizationTests.cs, silently granting this
    // "Employee" user CompanyAdministrator too since roles are additive and never reset between
    // test classes sharing the same Testcontainer database. See that file's NoRoleUser comment
    // for the same precedent.
    private static readonly Guid HrAdminUserId = Guid.NewGuid();
    private static readonly Guid CompanyAdminUserId = Guid.NewGuid();
    private static readonly Guid EmployeeUserId = Guid.NewGuid();

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

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
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
        using var client = await ClientFor(companyId, EmployeeUserId);

        var response = await client.GetAsync("/api/company-onboarding/checklist");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Checklist_Returns_Ok_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUserId);

        var response = await client.GetAsync("/api/company-onboarding/checklist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ChecklistPayload>();
        Assert.NotNull(payload);
        Assert.Equal(9, payload!.Tasks.Count);
        Assert.InRange(payload.CompletionPercentage, 0, 100);
    }

    [Fact]
    public async Task Get_Checklist_Returns_Ok_For_CompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdminUserId);

        var response = await client.GetAsync("/api/company-onboarding/checklist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ChecklistPayload>();
        Assert.NotNull(payload);
        Assert.Equal(9, payload!.Tasks.Count);
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
