using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ResetProgrammaticTaskCompletionEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid OperatorUser = new("cc0000d2-0000-0000-0000-000000000001");
    private static readonly Guid HrAdminUser = new("cc0000d2-0000-0000-0000-000000000002");

    public ResetProgrammaticTaskCompletionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, OperatorUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
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

    private static string Url(Guid companyId, Guid operationId) =>
        $"/api/companies/{companyId}/tasks/programmatic-completions/{operationId}/reset";

    [Fact]
    public async Task Post_Reset_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid(), Guid.NewGuid()), new { reason = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reset_Returns_Forbidden_For_User_Without_Company_Manage_Permission()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var client = await ClientAsync(HrAdminUser, SystemRoles.HrAdministrator, blocked.CompanyId);

        var response = await client.PostAsJsonAsync(Url(blocked.CompanyId, blocked.TasksOperationId), new { reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True((await StateAsync(blocked.TaskId)).IsTerminallyFailed);
    }

    [Fact]
    public async Task Post_Reset_Returns_NotFound_For_Unknown_Operation_And_Operations_Of_Other_Companies()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        var otherCompany = Guid.NewGuid();
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, otherCompany);

        var unknown = await client.PostAsJsonAsync(Url(otherCompany, Guid.NewGuid()), new { reason = "x" });
        var crossTenant = await client.PostAsJsonAsync(Url(otherCompany, blocked.TasksOperationId), new { reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        Assert.True((await StateAsync(blocked.TaskId)).IsTerminallyFailed);
    }

    [Fact]
    public async Task Post_Reset_Returns_UnprocessableEntity_When_Reason_Is_Missing()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);

        var response = await client.PostAsJsonAsync(Url(blocked.CompanyId, blocked.TasksOperationId), new { reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await StateAsync(blocked.TaskId)).IsTerminallyFailed);
    }

    [Fact]
    public async Task Post_Reset_Clears_The_Terminal_State_And_A_Repeat_Request_Is_Idempotent()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);

        var first = await client.PostAsJsonAsync(Url(blocked.CompanyId, blocked.TasksOperationId), new { reason = "Fixed upstream" });
        var second = await client.PostAsJsonAsync(Url(blocked.CompanyId, blocked.TasksOperationId), new { reason = "Fixed upstream" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((await first.Content.ReadFromJsonAsync<ResetPayload>())!.WasReset);
        Assert.False((await second.Content.ReadFromJsonAsync<ResetPayload>())!.WasReset);

        var state = await StateAsync(blocked.TaskId);
        Assert.False(state.IsTerminallyFailed);
        Assert.Null(state.FailureReason);
        Assert.Equal(1, state.ResetCount);
        Assert.Equal(OperatorUser, state.LastResetBy);
    }

    private async Task<HR.Modules.Tasks.Domain.ProgrammaticTaskCompletion> StateAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .ProgrammaticTaskCompletions.AsNoTracking().SingleAsync(c => c.TaskId == taskId);
    }

    private sealed record ResetPayload(Guid OperationId, Guid? TaskId, bool WasReset);
}
