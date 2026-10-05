using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class MultiStageInterviewKanbanEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd0000a1-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public MultiStageInterviewKanbanEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Seed> SeedAsync(bool secondInterviewStage = true)
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var received = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.NewApplication);
        var cvReview = RecruitmentStage.Create(Guid.NewGuid(), companyId, "CV Review", 2, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.CvReview);
        var first = RecruitmentStage.Create(Guid.NewGuid(), companyId, "First Interview", 3, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        var second = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Second Interview", 4, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        if (!secondInterviewStage)
            second.SetActiveStatus(false, Now);
        var offer = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Offer", 5, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);
        var hired = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Hired", 6, true, RecruitmentStageTerminalOutcome.Hired, Now);
        var rejected = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected", 7, true, RecruitmentStageTerminalOutcome.Rejected, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, cvReview.Id, null, Now);
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

    private static async Task RecordAsync(HttpClient client, Seed s, Guid interviewId, string outcome)
    {
        var response = await client.PostAsJsonAsync(Url(s, $"/interviews/{interviewId}/outcome"), new
        {
            companyId = s.CompanyId,
            vacancyId = s.VacancyId,
            applicationId = s.ApplicationId,
            interviewId,
            outcome,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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

    private static async Task<KanbanCard> CardAsync(HttpClient client, Seed s)
    {
        var response = await client.GetAsync($"/api/companies/{s.CompanyId}/vacancies/{s.VacancyId}/kanban");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<KanbanPayload>();
        return payload!.Columns.SelectMany(c => c.Candidates).Single(c => c.ApplicationId == s.ApplicationId);
    }

    [Fact]
    public async Task Two_Interview_Stages_Run_Schedule_Pass_Progress_Pass_Offer_With_Stage_Specific_Kanban_State()
    {
        var s = await SeedAsync();
        using var client = await ClientAsync(s.CompanyId);

        var firstInterview = await ScheduleAsync(client, s);
        var card = await CardAsync(client, s);
        Assert.Equal(s.FirstId, card.StageId);
        Assert.True(card.CurrentStageHasPendingInterview);
        Assert.Equal(firstInterview, card.PendingInterviewId);
        Assert.True(card.HasNextInterviewStage);
        Assert.Equal(s.SecondId, card.NextInterviewStageId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            Assert.Equal(s.FirstId, (await db.Interviews.SingleAsync(i => i.Id == firstInterview)).StageId);
        }

        await RecordAsync(client, s, firstInterview, "Passed");
        card = await CardAsync(client, s);
        Assert.False(card.CurrentStageHasPendingInterview);
        Assert.Equal("Passed", card.CurrentStageInterviewOutcome);
        Assert.True(card.HasNextInterviewStage);

        Assert.Equal(HttpStatusCode.BadRequest, (await OfferAsync(client, s)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(client, s, s.SecondId)).StatusCode);
        card = await CardAsync(client, s);
        Assert.Equal(s.SecondId, card.StageId);
        Assert.Equal("Passed", card.InterviewOutcome);
        Assert.Null(card.CurrentStageInterviewOutcome);
        Assert.False(card.CurrentStageHasPendingInterview);
        Assert.False(card.HasNextInterviewStage);
        Assert.Equal(HttpStatusCode.BadRequest, (await OfferAsync(client, s)).StatusCode);

        var secondInterview = await ScheduleAsync(client, s);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            Assert.Equal(s.SecondId, (await db.Interviews.SingleAsync(i => i.Id == secondInterview)).StageId);
        }

        await RecordAsync(client, s, secondInterview, "Passed");
        card = await CardAsync(client, s);
        Assert.Equal("Passed", card.CurrentStageInterviewOutcome);
        Assert.False(card.HasNextInterviewStage);

        var offerResponse = await OfferAsync(client, s);
        Assert.True(offerResponse.StatusCode == HttpStatusCode.OK, await offerResponse.Content.ReadAsStringAsync());
        Assert.Equal(s.OfferId, (await CardAsync(client, s)).StageId);
    }

    [Fact]
    public async Task Recording_One_Outcome_Does_Not_Hide_Another_Pending_Interview_In_The_Same_Stage()
    {
        var s = await SeedAsync(secondInterviewStage: false);
        using var client = await ClientAsync(s.CompanyId);

        var one = await ScheduleAsync(client, s);
        var two = await ScheduleAsync(client, s);
        await RecordAsync(client, s, one, "Passed");

        var card = await CardAsync(client, s);
        Assert.True(card.CurrentStageHasPendingInterview);
        Assert.Equal(two, card.PendingInterviewId);
        Assert.Equal("Pending", card.InterviewOutcome);
        Assert.Equal(HttpStatusCode.BadRequest, (await OfferAsync(client, s)).StatusCode);
    }

    [Fact]
    public async Task Single_Interview_Stage_Company_Offers_After_Pass()
    {
        var s = await SeedAsync(secondInterviewStage: false);
        using var client = await ClientAsync(s.CompanyId);

        var interview = await ScheduleAsync(client, s);
        await RecordAsync(client, s, interview, "Passed");

        var card = await CardAsync(client, s);
        Assert.False(card.HasNextInterviewStage);
        var offerResponse = await OfferAsync(client, s);
        Assert.True(offerResponse.StatusCode == HttpStatusCode.OK, await offerResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Scheduling_Again_Is_Rejected_After_Pass_When_Next_Interview_Stage_Exists()
    {
        var s = await SeedAsync();
        using var client = await ClientAsync(s.CompanyId);

        var interview = await ScheduleAsync(client, s);
        await RecordAsync(client, s, interview, "Passed");

        var response = await client.PostAsJsonAsync(Url(s, "/interviews"), new
        {
            companyId = s.CompanyId,
            vacancyId = s.VacancyId,
            applicationId = s.ApplicationId,
            interviewerEmployeeId = Guid.NewGuid(),
            scheduledAt = DateTimeOffset.UtcNow.AddDays(2),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Generic_Move_To_Terminal_Stage_Is_Rejected()
    {
        var s = await SeedAsync();
        using var client = await ClientAsync(s.CompanyId);

        var response = await MoveAsync(client, s, s.HiredId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reject_From_Kanban_Moves_Application_To_Rejected_Stage()
    {
        var s = await SeedAsync();
        using var client = await ClientAsync(s.CompanyId);

        var response = await client.PostAsJsonAsync(Url(s, "/reject"), new
        {
            companyId = s.CompanyId,
            vacancyId = s.VacancyId,
            applicationId = s.ApplicationId,
            rejectionReason = "Not a fit",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rejected", (await CardAsync(client, s)).StageName);
    }

    [Fact]
    public async Task Withdraw_From_Kanban_Flags_Application_As_Withdrawn()
    {
        var s = await SeedAsync();
        using var client = await ClientAsync(s.CompanyId);

        var response = await client.DeleteAsync(Url(s));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await CardAsync(client, s)).IsWithdrawn);
    }

    [Fact]
    public async Task Schedule_Interview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}/interviews", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);
    private sealed record KanbanPayload(List<KanbanColumn> Columns);
    private sealed record KanbanColumn(List<KanbanCard> Candidates);
    private sealed record KanbanCard(
        Guid ApplicationId, Guid StageId, string StageName, bool IsWithdrawn, string? InterviewOutcome,
        bool CurrentStageHasPendingInterview, Guid? PendingInterviewId, string? CurrentStageInterviewOutcome,
        bool HasNextInterviewStage, Guid? NextInterviewStageId);
}
