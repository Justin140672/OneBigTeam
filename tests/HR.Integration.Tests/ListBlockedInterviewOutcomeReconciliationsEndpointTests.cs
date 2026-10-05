using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListBlockedInterviewOutcomeReconciliationsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid OperatorUser = new("cc0000d4-0000-0000-0000-000000000001");
    private static readonly Guid RecruiterUser = new("cc0000d4-0000-0000-0000-000000000002");

    public ListBlockedInterviewOutcomeReconciliationsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, OperatorUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAsync(Guid userId, Guid roleId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    private static string Url(Guid companyId) =>
        $"/api/companies/{companyId}/recruitment/interview-outcome-reconciliations/blocked";

    [Fact]
    public async Task Get_Blocked_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Blocked_Returns_Forbidden_For_User_Without_Company_Manage_Permission()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAsync(RecruiterUser, SystemRoles.Recruiter, companyId);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Blocked_Lists_Only_This_Companys_Blocked_Reconciliations_With_Recovery_Identifiers()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        var other = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);

        var response = await client.GetAsync(Url(blocked.CompanyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        var item = Assert.Single(payload!.Items);
        Assert.Equal(blocked.ReconciliationId, item.ReconciliationId);
        Assert.Equal(blocked.InterviewId, item.InterviewId);
        Assert.Equal(blocked.TaskId, item.TaskId);
        Assert.Equal(blocked.TasksOperationId, item.TasksOperationId);
        Assert.Equal("task_terminal_failure", item.Category);
        Assert.DoesNotContain(payload.Items, i => i.ReconciliationId == other.ReconciliationId);
    }

    [Fact]
    public async Task Get_Blocked_Returns_An_Empty_List_When_Nothing_Is_Blocked()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<ListPayload>())!.Items);
    }

    private sealed record ListPayload(List<ItemPayload> Items);

    private sealed record ItemPayload(
        Guid ReconciliationId, Guid InterviewId, Guid ApplicationId, Guid? TaskId, Guid? TasksOperationId,
        string? Category, string? FailureReason, int AttemptCount, DateTimeOffset BlockedAt);
}
