using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for DELETE /vacancies/{v}/applications/{a} (withdraw). See
/// WithdrawApplicationHandlerTests in HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 200 + WithdrawnAt set + pending interview cancelled,
/// unknown application 404, cross-company 404, already-withdrawn 400, terminal-stage 400.
/// </summary>
[Collection("Integration")]
public class WithdrawApplicationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c4-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c4-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public WithdrawApplicationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private static string Url(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}";

    [Fact]
    public async Task Delete_Application_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.DeleteAsync(Url(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Application_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, seeded.VacancyId, seeded.ApplicationId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Application_Withdraws_And_Cancels_Pending_Interview()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, seeded.VacancyId, seeded.ApplicationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WithdrawPayload>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.WithdrawnAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var savedApp = await db.Applications.SingleAsync(a => a.Id == seeded.ApplicationId);
        Assert.NotNull(savedApp.WithdrawnAt);
        var savedInterview = await db.Interviews.SingleAsync(i => i.Id == interviewId);
        Assert.Equal(InterviewOutcome.Cancelled, savedInterview.Outcome);
    }

    [Fact]
    public async Task Delete_Application_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, seeded.VacancyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Application_Returns_NotFound_For_Application_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        using var client = await ClientAs(RecruiterUser, companyB);

        var response = await client.DeleteAsync(Url(companyB, seeded.VacancyId, seeded.ApplicationId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Application_Returns_BadRequest_When_Already_Withdrawn()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        await RecruitmentTestSeeder.WithdrawApplicationAsync(_factory, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, seeded.VacancyId, seeded.ApplicationId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Application_Returns_BadRequest_When_On_Terminal_Stage()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        await RecruitmentTestSeeder.MarkApplicationOnStageAsync(_factory, seeded.ApplicationId, seeded.HiredStageId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.DeleteAsync(Url(companyId, seeded.VacancyId, seeded.ApplicationId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record WithdrawPayload(
        Guid Id, Guid VacancyId, Guid CandidateId, Guid CurrentStageId, DateTimeOffset? WithdrawnAt);
}
