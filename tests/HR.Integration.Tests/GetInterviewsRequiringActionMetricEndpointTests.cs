using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetInterviewsRequiringActionMetricEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("db040004-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("db040004-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetInterviewsRequiringActionMetricEndpointTests(ApiWebApplicationFactory factory)
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

    private string Url(Guid companyId) => $"/api/companies/{companyId}/recruitment/metrics/interviews-requiring-action";

    private static (Vacancy Vacancy, Candidate Candidate, Application Application) Graph(RecruitmentDbContext db, Guid companyId, Guid stageId)
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "QA Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Ana", "Ng", $"ana.{Guid.NewGuid():N}@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stageId, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        return (vacancy, candidate, application);
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Url(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(PlainEmployeeUser, companyId);
        var response = await client.GetAsync(Url(companyId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Counts_Overdue_Pending_Interviews_Excluding_Future_And_Resolved()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now).ToList();
            var interviewStage = stages.Single(s => s.Name == "Interview");
            db.RecruitmentStages.AddRange(stages);

            var g1 = Graph(db, companyId, interviewStage.Id);
            var g2 = Graph(db, companyId, interviewStage.Id);
            var g3 = Graph(db, companyId, interviewStage.Id);
            var g4 = Graph(db, companyId, interviewStage.Id);

            var overdue = Interview.Create(Guid.NewGuid(), companyId, g1.Application.Id, Guid.NewGuid(), Now.AddDays(-1), 30, "Room 1", Now);
            var future = Interview.Create(Guid.NewGuid(), companyId, g2.Application.Id, Guid.NewGuid(), Now.AddDays(3), 30, "Room 2", Now);
            var passed = Interview.Create(Guid.NewGuid(), companyId, g3.Application.Id, Guid.NewGuid(), Now.AddDays(-2), 30, "Room 3", Now);
            passed.RecordOutcome(InterviewOutcome.Passed, null, Now);
            var cancelled = Interview.Create(Guid.NewGuid(), companyId, g4.Application.Id, Guid.NewGuid(), Now.AddDays(-2), 30, "Room 4", Now);
            cancelled.Cancel(Now);

            db.Interviews.AddRange(overdue, future, passed, cancelled);
            await db.SaveChangesAsync();
        }

        var payload = await (await client.GetAsync(Url(companyId))).Content.ReadFromJsonAsync<MetricPayload>();

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Count);
        Assert.Equal(payload.Count, payload.Items.Count);
    }

    [Fact]
    public async Task Rescheduled_Interview_Moved_Into_The_Past_Requires_Action()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            // Custom single-stage pipeline.
            var stage = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Meeting the team", 1, false, RecruitmentStageTerminalOutcome.None, Now);
            db.RecruitmentStages.Add(stage);
            var g = Graph(db, companyId, stage.Id);
            var interview = Interview.Create(Guid.NewGuid(), companyId, g.Application.Id, Guid.NewGuid(), Now.AddDays(5), 30, "Room 1", Now);
            interview.UpdateDetails(Guid.NewGuid(), Now.AddDays(-1), 30, "Room 1", Now);
            db.Interviews.Add(interview);
            await db.SaveChangesAsync();
        }

        var payload = await (await client.GetAsync(Url(companyId))).Content.ReadFromJsonAsync<MetricPayload>();

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Count);
    }

    [Fact]
    public async Task Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var stages = RecruitmentStageSeeder.BuildDefaultStages(otherCompanyId, Now).ToList();
            var interviewStage = stages.Single(s => s.Name == "Interview");
            db.RecruitmentStages.AddRange(stages);
            var g = Graph(db, otherCompanyId, interviewStage.Id);
            db.Interviews.Add(Interview.Create(Guid.NewGuid(), otherCompanyId, g.Application.Id, Guid.NewGuid(), Now.AddDays(-1), 30, "Room 1", Now));
            await db.SaveChangesAsync();
        }

        var payload = await (await client.GetAsync(Url(companyId))).Content.ReadFromJsonAsync<MetricPayload>();

        Assert.NotNull(payload);
        Assert.Equal(0, payload!.Count);
        Assert.Empty(payload.Items);
    }

    private sealed record MetricPayload(int Count, List<ItemPayload> Items);
    private sealed record ItemPayload(Guid InterviewId, Guid ApplicationId, Guid VacancyId);
}
