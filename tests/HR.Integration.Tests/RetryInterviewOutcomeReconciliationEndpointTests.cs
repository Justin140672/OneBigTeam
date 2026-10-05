using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RetryInterviewOutcomeReconciliationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid OperatorUser = new("cc0000d3-0000-0000-0000-000000000001");
    private static readonly Guid RecruiterUser = new("cc0000d3-0000-0000-0000-000000000002");

    public RetryInterviewOutcomeReconciliationEndpointTests(ApiWebApplicationFactory factory)
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

    private static string Url(Guid companyId, Guid reconciliationId) =>
        $"/api/companies/{companyId}/recruitment/interview-outcome-reconciliations/{reconciliationId}/retry";

    [Fact]
    public async Task Post_Retry_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid(), Guid.NewGuid()), new { reason = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Retry_Returns_Forbidden_For_User_Without_Company_Manage_Permission()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var client = await ClientAsync(RecruiterUser, SystemRoles.Recruiter, blocked.CompanyId);

        var response = await client.PostAsJsonAsync(Url(blocked.CompanyId, blocked.ReconciliationId), new { reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True((await RecordAsync(blocked.ReconciliationId)).IsBlocked);
    }

    [Fact]
    public async Task Post_Retry_Returns_NotFound_For_Unknown_And_Cross_Tenant_Reconciliations()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        var otherCompany = Guid.NewGuid();
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, otherCompany);

        var unknown = await client.PostAsJsonAsync(Url(otherCompany, Guid.NewGuid()), new { reason = "x" });
        var crossTenant = await client.PostAsJsonAsync(Url(otherCompany, blocked.ReconciliationId), new { reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        Assert.True((await RecordAsync(blocked.ReconciliationId)).IsBlocked);
    }

    [Fact]
    public async Task Post_Retry_Returns_UnprocessableEntity_When_Reason_Is_Missing()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);

        var response = await client.PostAsJsonAsync(Url(blocked.CompanyId, blocked.ReconciliationId), new { reason = " " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await RecordAsync(blocked.ReconciliationId)).IsBlocked);
    }

    [Fact]
    public async Task Post_Retry_Resets_The_Tasks_Operation_Unblocks_And_Is_Idempotent_On_Repeat()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);

        var first = await client.PostAsJsonAsync(Url(blocked.CompanyId, blocked.ReconciliationId), new { reason = "Data corrected" });
        var second = await client.PostAsJsonAsync(Url(blocked.CompanyId, blocked.ReconciliationId), new { reason = "Data corrected" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstPayload = await first.Content.ReadFromJsonAsync<RetryPayload>();
        Assert.True(firstPayload!.WasBlocked);
        Assert.True(firstPayload.TasksCompletionReset);
        Assert.Equal("completed", firstPayload.Status);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await second.Content.ReadFromJsonAsync<RetryPayload>())!.WasBlocked);

        var record = await RecordAsync(blocked.ReconciliationId);
        Assert.False(record.IsBlocked);
        Assert.Equal(1, record.RepairCount);
        Assert.Equal(OperatorUser, record.LastRepairedBy);

        using var scope = _factory.Services.CreateScope();
        var state = await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .ProgrammaticTaskCompletions.AsNoTracking().SingleAsync(c => c.TaskId == blocked.TaskId);
        Assert.Equal(1, state.ResetCount);
        Assert.NotNull(state.ConfirmedAt);
    }

    private async Task<HR.Modules.Recruitment.Domain.InterviewOutcomeTaskReconciliation> RecordAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>()
            .InterviewOutcomeTaskReconciliations.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private sealed record RetryPayload(Guid ReconciliationId, Guid InterviewId, string Status, bool WasBlocked, bool TasksCompletionReset);
}
