using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class InterviewStageEnforcementEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd0000b1-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public InterviewStageEnforcementEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(() => TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter)).GetAwaiter().GetResult();
    }

    private sealed record Seed(
        Guid CompanyId, Guid VacancyId, Guid ApplicationId,
        Guid CvReviewId, Guid FirstId, Guid SecondId, Guid OfferId, Guid HiredId);

    private async Task<HttpClient> ClientAsync(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, RecruiterUser, companyId);
        return client;
    }

    private async Task<Seed> SeedAsync(Func<Guid, Guid, Guid, Guid, Guid>? startStage = null)
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var received = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.NewApplication);
        var cvReview = RecruitmentStage.Create(Guid.NewGuid(), companyId, "CV Review", 2, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.CvReview);
        var first = RecruitmentStage.Create(Guid.NewGuid(), companyId, "First Interview", 3, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        var second = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Second Interview", 4, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        var offer = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Offer", 5, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);
        var hired = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Hired", 6, true, RecruitmentStageTerminalOutcome.Hired, Now);
        var rejected = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected", 7, true, RecruitmentStageTerminalOutcome.Rejected, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, Now);
        var start = startStage?.Invoke(cvReview.Id, first.Id, second.Id, offer.Id) ?? cvReview.Id;
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, start, null, Now);
        db.RecruitmentStages.AddRange(received, cvReview, first, second, offer, hired, rejected);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return new Seed(companyId, vacancy.Id, application.Id, cvReview.Id, first.Id, second.Id, offer.Id, hired.Id);
    }

    private static string Url(Seed s, string suffix = "") =>
        $"/api/companies/{s.CompanyId}/vacancies/{s.VacancyId}/applications/{s.ApplicationId}{suffix}";

    private static async Task<Guid> ScheduleAsync(HttpClient client, Seed s)
    {
        var response = await client.PostAsJsonAsync(Url(s, "/interviews"), new
        {
            companyId = s.CompanyId,
            vacancyId = s.VacancyId,
            applicationId = s.ApplicationId,
            interviewerEmployeeId = Guid.NewGuid(),
            scheduledAt = DateTimeOffset.UtcNow.AddDays(1),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static Task<HttpResponseMessage> RecordRawAsync(HttpClient client, Seed s, Guid interviewId, string outcome) =>
        client.PostAsJsonAsync(Url(s, $"/interviews/{interviewId}/outcome"), new
        {
            companyId = s.CompanyId,
            vacancyId = s.VacancyId,
            applicationId = s.ApplicationId,
            interviewId,
            outcome,
        });

    private static async Task RecordAsync(HttpClient client, Seed s, Guid interviewId, string outcome) =>
        Assert.Equal(HttpStatusCode.OK, (await RecordRawAsync(client, s, interviewId, outcome)).StatusCode);

    private static Task<HttpResponseMessage> OfferAsync(HttpClient client, Seed s) =>
        client.PostAsJsonAsync(Url(s, "/offer"), new { companyId = s.CompanyId, vacancyId = s.VacancyId, applicationId = s.ApplicationId, offeredSalaryFrequency = "Annual" });

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Seed s, Guid stageId) =>
        client.PostAsJsonAsync(Url(s, "/move-stage"), new
        {
            companyId = s.CompanyId,
            vacancyId = s.VacancyId,
            applicationId = s.ApplicationId,
            newStageId = stageId,
        });

    private async Task<Guid> CurrentStageAsync(Seed s)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return (await db.Applications.AsNoTracking().SingleAsync(a => a.Id == s.ApplicationId)).CurrentStageId;
    }

    [Fact]
    public async Task Skipping_First_Interview_Stage_Is_Rejected()
    {
        var s = await SeedAsync();
        using var client = await ClientAsync(s.CompanyId);

        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(client, s, s.SecondId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(client, s, s.OfferId)).StatusCode);
        Assert.Equal(s.CvReviewId, await CurrentStageAsync(s));
    }

    [Fact]
    public async Task Moving_From_First_To_Second_Without_Pass_Is_Rejected_And_Allowed_After_Pass()
    {
        var s = await SeedAsync((_, first, _, _) => first);
        using var client = await ClientAsync(s.CompanyId);

        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(client, s, s.SecondId)).StatusCode);

        var interview = await ScheduleAsync(client, s);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(client, s, s.SecondId)).StatusCode);

        await RecordAsync(client, s, interview, "Passed");
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(client, s, s.SecondId)).StatusCode);
    }

    [Fact]
    public async Task Skipping_Second_Interview_Stage_To_Offer_Is_Rejected_By_Move_And_Offer_Api()
    {
        var s = await SeedAsync((_, first, _, _) => first);
        using var client = await ClientAsync(s.CompanyId);
        var interview = await ScheduleAsync(client, s);
        await RecordAsync(client, s, interview, "Passed");

        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(client, s, s.OfferId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await OfferAsync(client, s)).StatusCode);
        Assert.Equal(s.FirstId, await CurrentStageAsync(s));
    }

    [Fact]
    public async Task Application_Manually_Placed_In_Offer_Stage_Cannot_Make_An_Offer()
    {
        var s = await SeedAsync((_, _, _, offer) => offer);
        using var client = await ClientAsync(s.CompanyId);

        Assert.Equal(HttpStatusCode.BadRequest, (await OfferAsync(client, s)).StatusCode);
    }

    [Fact]
    public async Task Offer_Succeeds_After_Final_Stage_Passed_Even_When_Application_Already_In_Offer_Stage()
    {
        var s = await SeedAsync((_, first, _, _) => first);
        using var client = await ClientAsync(s.CompanyId);

        await RecordAsync(client, s, await ScheduleAsync(client, s), "Passed");
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(client, s, s.SecondId)).StatusCode);
        await RecordAsync(client, s, await ScheduleAsync(client, s), "Passed");
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(client, s, s.OfferId)).StatusCode);

        var offer = await OfferAsync(client, s);
        Assert.True(offer.StatusCode == HttpStatusCode.OK, await offer.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("NoShow")]
    public async Task Offer_Is_Rejected_When_Final_Interview_Did_Not_Pass(string outcome)
    {
        var s = await SeedAsync((_, first, _, _) => first);
        using var client = await ClientAsync(s.CompanyId);

        await RecordAsync(client, s, await ScheduleAsync(client, s), "Passed");
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(client, s, s.SecondId)).StatusCode);
        await RecordAsync(client, s, await ScheduleAsync(client, s), outcome);

        Assert.Equal(HttpStatusCode.BadRequest, (await OfferAsync(client, s)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(client, s, s.OfferId)).StatusCode);
    }

    [Fact]
    public async Task Applications_List_Returns_Stage_Specific_State_After_Progression()
    {
        var s = await SeedAsync((_, first, _, _) => first);
        using var client = await ClientAsync(s.CompanyId);

        await RecordAsync(client, s, await ScheduleAsync(client, s), "Passed");
        var afterFirst = await ListItemAsync(client, s);
        Assert.Equal("Passed", afterFirst.CurrentStageInterviewOutcome);
        Assert.True(afterFirst.HasNextInterviewStage);
        Assert.Equal(s.SecondId, afterFirst.NextInterviewStageId);
        Assert.False(afterFirst.AllRequiredInterviewStagesPassed);

        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(client, s, s.SecondId)).StatusCode);
        var inSecond = await ListItemAsync(client, s);
        Assert.Equal("Passed", inSecond.InterviewOutcome);
        Assert.Null(inSecond.CurrentStageInterviewOutcome);
        Assert.False(inSecond.CurrentStageHasPendingInterview);
        Assert.False(inSecond.AllRequiredInterviewStagesPassed);

        var pending = await ScheduleAsync(client, s);
        var withPending = await ListItemAsync(client, s);
        Assert.True(withPending.CurrentStageHasPendingInterview);
        Assert.Equal(pending, withPending.PendingInterviewId);

        await RecordAsync(client, s, pending, "Passed");
        var done = await ListItemAsync(client, s);
        Assert.Equal("Passed", done.CurrentStageInterviewOutcome);
        Assert.True(done.AllRequiredInterviewStagesPassed);
    }

    [Fact]
    public async Task Reject_Cancels_Pending_Interviews_Tasks_And_Removes_Them_From_Upcoming_And_Action_Queues()
    {
        var s = await SeedAsync((_, first, _, _) => first);
        using var client = await ClientAsync(s.CompanyId);

        var completed = await ScheduleAsync(client, s);
        await RecordAsync(client, s, completed, "Failed");
        var pending = await ScheduleAsync(client, s);
        Guid overdue;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var late = Interview.Create(Guid.NewGuid(), s.CompanyId, s.ApplicationId, Guid.NewGuid(), Now.AddDays(-3), 30, null, Now.AddDays(-4), s.FirstId);
            db.Interviews.Add(late);
            await db.SaveChangesAsync();
            overdue = late.Id;
        }

        var before = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{s.CompanyId}/interviews/upcoming");
        Assert.Contains(before!.Items, i => i.InterviewId == pending);

        var reject = await client.PostAsJsonAsync(Url(s, "/reject"), new
        {
            companyId = s.CompanyId, vacancyId = s.VacancyId, applicationId = s.ApplicationId, rejectionReason = "Not a fit",
        });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var interviews = await db.Interviews.AsNoTracking().Where(i => i.ApplicationId == s.ApplicationId).ToListAsync();
            Assert.Equal(InterviewOutcome.Failed, interviews.Single(i => i.Id == completed).Outcome);
            Assert.Equal(InterviewOutcome.Cancelled, interviews.Single(i => i.Id == pending).Outcome);
            Assert.Equal(InterviewOutcome.Cancelled, interviews.Single(i => i.Id == overdue).Outcome);
            Assert.Equal(s.FirstId, interviews.Single(i => i.Id == completed).StageId);

            var tasksDb = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            var tasks = await tasksDb.TaskItems.AsNoTracking().Where(t => t.SourceEntityId == pending).ToListAsync();
            Assert.NotEmpty(tasks);
            Assert.All(tasks, t => Assert.Equal(HR.Modules.Tasks.Domain.TaskItemStatus.Cancelled, t.Status));
        }

        var upcoming = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{s.CompanyId}/interviews/upcoming");
        Assert.DoesNotContain(upcoming!.Items, i => i.InterviewId == pending);

        var metric = await client.GetFromJsonAsync<MetricPayload>($"/api/companies/{s.CompanyId}/recruitment/metrics/interviews-requiring-action");
        Assert.DoesNotContain(metric!.Items, i => i.InterviewId == overdue);
    }

    [Fact]
    public async Task Concurrent_Move_And_Outcome_Recording_Leave_Consistent_State()
    {
        var s = await SeedAsync((_, first, _, _) => first);
        using var client = await ClientAsync(s.CompanyId);
        using var other = await ClientAsync(s.CompanyId);
        var interview = await ScheduleAsync(client, s);

        var record = RecordRawAsync(client, s, interview, "Passed");
        var move = MoveAsync(other, s, s.SecondId);
        await Task.WhenAll(record, move);

        Assert.Equal(HttpStatusCode.OK, record.Result.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, move.Result.StatusCode);
        var stage = await CurrentStageAsync(s);
        if (move.Result.StatusCode == HttpStatusCode.OK)
            Assert.Equal(s.SecondId, stage);
        else
            Assert.Equal(s.FirstId, stage);
    }

    [Fact]
    public async Task Move_Stage_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}/move-stage", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<ListItem> ListItemAsync(HttpClient client, Seed s)
    {
        var payload = await client.GetFromJsonAsync<ListResponse>($"/api/companies/{s.CompanyId}/vacancies/{s.VacancyId}/applications");
        return payload!.Items.Single(i => i.Id == s.ApplicationId);
    }

    private sealed record IdPayload(Guid Id);
    private sealed record ListResponse(List<ListItem> Items);
    private sealed record ListItem(
        Guid Id, string? InterviewOutcome, bool CurrentStageHasPendingInterview, Guid? PendingInterviewId,
        string? CurrentStageInterviewOutcome, bool HasNextInterviewStage, Guid? NextInterviewStageId,
        bool AllRequiredInterviewStagesPassed);
    private sealed record ListPayload(List<InterviewItem> Items);
    private sealed record InterviewItem(Guid InterviewId);
    private sealed record MetricPayload(int Count, List<InterviewItem> Items);
}
