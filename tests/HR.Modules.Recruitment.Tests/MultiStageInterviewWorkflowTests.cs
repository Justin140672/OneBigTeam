using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetRecruitmentKanban;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Features.ScheduleInterview;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class MultiStageInterviewWorkflowTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private sealed record Seed(
        Guid CompanyId,
        Vacancy Vacancy,
        Application Application,
        RecruitmentStage CvReview,
        RecruitmentStage First,
        RecruitmentStage Second,
        RecruitmentStage Offer);

    private static async Task<Seed> SeedAsync(
        RecruitmentDbContext db, Func<RecruitmentStage, RecruitmentStage, RecruitmentStage, RecruitmentStage, RecruitmentStage> startStage,
        bool secondInterviewStage = true)
    {
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var received = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.NewApplication);
        var cvReview = RecruitmentStage.Create(Guid.NewGuid(), companyId, "CV Review", 2, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.CvReview);
        var first = RecruitmentStage.Create(Guid.NewGuid(), companyId, "First Interview", 3, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        var second = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Second Interview", 4, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        if (!secondInterviewStage)
            second.SetActiveStatus(false, Now);
        var offer = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Offer", 5, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);
        var hired = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Hired", 6, true, RecruitmentStageTerminalOutcome.Hired, Now);
        var rejected = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected", 7, true, RecruitmentStageTerminalOutcome.Rejected, Now);
        db.RecruitmentStages.AddRange(received, cvReview, first, second, offer, hired, rejected);

        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma@example.com", null, Now);
        var application = Application.Create(
            Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, startStage(cvReview, first, second, offer).Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        return new Seed(companyId, vacancy, application, cvReview, first, second, offer);
    }

    private static Interview AddInterview(
        RecruitmentDbContext db, Seed seed, RecruitmentStage stage, InterviewOutcome outcome, int dayOffset = 0)
    {
        var interview = Interview.Create(
            Guid.NewGuid(), seed.CompanyId, seed.Application.Id, Guid.NewGuid(), Now.AddDays(dayOffset), 30, null, Now, stage.Id);
        if (outcome != InterviewOutcome.Pending)
        {
            if (outcome == InterviewOutcome.Cancelled) interview.Cancel(Now);
            else interview.RecordOutcome(outcome, null, Now);
        }

        db.Interviews.Add(interview);
        return interview;
    }

    private static ScheduleInterviewRequest ScheduleRequest(Seed seed) => new()
    {
        CompanyId = seed.CompanyId,
        VacancyId = seed.Vacancy.Id,
        ApplicationId = seed.Application.Id,
        InterviewerEmployeeId = Guid.NewGuid(),
        ScheduledAt = Now.AddDays(3),
    };

    private static OfferCandidateRequest OfferRequest(Seed seed) => new()
    {
        CompanyId = seed.CompanyId,
        VacancyId = seed.Vacancy.Id,
        ApplicationId = seed.Application.Id,
    };

    [Fact]
    public async Task Schedule_From_CvReview_Moves_To_First_Interview_Stage_And_Tags_Interview_With_It()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (cv, _, _, _) => cv);

        var result = await ScheduleHandler(db).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var interview = await db.Interviews.SingleAsync();
        Assert.Equal(seed.First.Id, interview.StageId);
        Assert.Equal(seed.First.Id, (await db.Applications.SingleAsync()).CurrentStageId);
    }

    [Fact]
    public async Task Schedule_In_Second_Interview_Stage_Tags_Interview_With_Second_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -5);
        await db.SaveChangesAsync();

        var result = await ScheduleHandler(db).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.Second.Id, (await db.Interviews.SingleAsync(i => i.Outcome == InterviewOutcome.Pending)).StageId);
        Assert.Equal(seed.Second.Id, (await db.Applications.SingleAsync()).CurrentStageId);
    }

    [Fact]
    public async Task Schedule_Is_Rejected_When_Current_Stage_Passed_And_Another_Interview_Stage_Follows()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed);
        await db.SaveChangesAsync();

        var result = await ScheduleHandler(db).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Single(await db.Interviews.ToListAsync());
    }

    [Fact]
    public async Task Schedule_Is_Allowed_Again_In_Final_Interview_Stage_After_Pass()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first, secondInterviewStage: false);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed);
        await db.SaveChangesAsync();

        var result = await ScheduleHandler(db).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Offer_Is_Rejected_In_Second_Stage_When_Only_First_Stage_Interview_Passed()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -5);
        await db.SaveChangesAsync();

        var result = await OfferHandler(db).HandleAsync(OfferRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(seed.Second.Id, (await db.Applications.SingleAsync()).CurrentStageId);
    }

    [Fact]
    public async Task Offer_Is_Rejected_From_First_Stage_When_Another_Interview_Stage_Follows()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed);
        await db.SaveChangesAsync();

        var result = await OfferHandler(db).HandleAsync(OfferRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Offer_Is_Rejected_While_Another_Interview_In_Current_Stage_Is_Pending()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        AddInterview(db, seed, seed.Second, InterviewOutcome.Passed, -2);
        AddInterview(db, seed, seed.Second, InterviewOutcome.Pending, 2);
        await db.SaveChangesAsync();

        var result = await OfferHandler(db).HandleAsync(OfferRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Offer_Is_Rejected_When_Current_Interview_Stage_Has_No_Passed_Interview()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        await db.SaveChangesAsync();

        var result = await OfferHandler(db).HandleAsync(OfferRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Offer_Succeeds_After_Final_Interview_Stage_Passed()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -5);
        AddInterview(db, seed, seed.Second, InterviewOutcome.Passed, -1);
        await db.SaveChangesAsync();

        var result = await OfferHandler(db).HandleAsync(OfferRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.Offer.Id, (await db.Applications.SingleAsync()).CurrentStageId);
    }

    [Fact]
    public async Task Offer_Succeeds_After_Single_Interview_Stage_Passed()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first, secondInterviewStage: false);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed);
        await db.SaveChangesAsync();

        var result = await OfferHandler(db).HandleAsync(OfferRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Offer_From_CvReview_Is_Blocked_When_Interview_Stages_Are_Configured()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (cv, _, _, _) => cv);

        var result = await OfferHandler(db).HandleAsync(OfferRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task RecordOutcome_Keeps_Application_Summary_Pending_While_Another_Interview_Is_Pending()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var one = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 1);
        AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 2);
        await db.SaveChangesAsync();

        var result = await RecordHandler(db).HandleAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId = seed.CompanyId,
                VacancyId = seed.Vacancy.Id,
                ApplicationId = seed.Application.Id,
                InterviewId = one.Id,
                Outcome = InterviewOutcome.Passed,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InterviewOutcome.Pending, (await db.Applications.SingleAsync()).InterviewOutcome);
    }

    [Fact]
    public async Task Kanban_Reports_Stage_Specific_State_For_Second_Stage_Ignoring_First_Stage_Pass()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -5);
        seed.Application.SetInterviewOutcome(InterviewOutcome.Passed, Now);
        await db.SaveChangesAsync();

        var card = await LoadCardAsync(db, seed);

        Assert.False(card.CurrentStageHasPendingInterview);
        Assert.Null(card.PendingInterviewId);
        Assert.Null(card.CurrentStageInterviewOutcome);
        Assert.False(card.HasNextInterviewStage);
        Assert.Equal("Passed", card.InterviewOutcome);
    }

    [Fact]
    public async Task Kanban_Reports_Next_Stage_And_Latest_Outcome_For_Passed_First_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed);
        await db.SaveChangesAsync();

        var card = await LoadCardAsync(db, seed);

        Assert.Equal("Passed", card.CurrentStageInterviewOutcome);
        Assert.True(card.HasNextInterviewStage);
        Assert.Equal(seed.Second.Id, card.NextInterviewStageId);
        Assert.Equal("Second Interview", card.NextInterviewStageName);
    }

    [Fact]
    public async Task Kanban_Reports_Pending_Interview_Id_When_One_Of_Several_Is_Pending()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);
        var pending = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 2);
        await db.SaveChangesAsync();

        var card = await LoadCardAsync(db, seed);

        Assert.True(card.CurrentStageHasPendingInterview);
        Assert.Equal(pending.Id, card.PendingInterviewId);
    }

    [Fact]
    public async Task Kanban_Has_No_Next_Stage_When_Only_One_Interview_Stage_Is_Active()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first, secondInterviewStage: false);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed);
        await db.SaveChangesAsync();

        var card = await LoadCardAsync(db, seed);

        Assert.False(card.HasNextInterviewStage);
        Assert.Equal("Passed", card.CurrentStageInterviewOutcome);
    }

    [Fact]
    public async Task Kanban_Reports_Accepted_Offer_And_Appointment_Status()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, _, offer) => offer);
        seed.Application.RecordOfferTerms(null, null, null, DateOnly.FromDateTime(Now.UtcDateTime), null, Now);
        seed.Application.RespondToOffer(OfferResponseStatus.Accepted, Now);
        await db.SaveChangesAsync();

        var card = await LoadCardAsync(db, seed);

        Assert.Equal("Accepted", card.OfferResponseStatus);
        Assert.False(card.IsInternal);
        Assert.Null(card.InternalAppointmentStatus);
    }

    [Fact]
    public void Evaluate_Ignores_Cancelled_And_NoShow_As_Passed()
    {
        var company = Guid.NewGuid();
        var stage = RecruitmentStage.Create(Guid.NewGuid(), company, "I", 1, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        var appId = Guid.NewGuid();
        var cancelled = Interview.Create(Guid.NewGuid(), company, appId, Guid.NewGuid(), Now, 30, null, Now, stage.Id);
        cancelled.Cancel(Now);
        var noShow = Interview.Create(Guid.NewGuid(), company, appId, Guid.NewGuid(), Now.AddDays(1), 30, null, Now, stage.Id);
        noShow.RecordOutcome(InterviewOutcome.NoShow, null, Now);

        var state = InterviewStageWorkflow.Evaluate(stage, [stage], [cancelled, noShow]);

        Assert.Equal(InterviewOutcome.NoShow, state.LatestOutcome);
        Assert.False(state.CurrentStageHasPendingInterview);
        Assert.NotNull(InterviewStageWorkflow.DescribeOfferViolation([stage], [cancelled, noShow]));
    }

    private static async Task<KanbanCandidateSummary> LoadCardAsync(RecruitmentDbContext db, Seed seed)
    {
        var result = await new GetRecruitmentKanbanHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetRecruitmentKanbanRequest { CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value!.Columns.SelectMany(c => c.Candidates).Single();
    }

    private static ScheduleInterviewHandler ScheduleHandler(RecruitmentDbContext db) =>
        new(db, new FakeNotificationWriter(), new FakeClock(FixedUtcNow), new FakePositionProfileReader(),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            new InterviewTaskEffectsService(db, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeTaskCompleter(), new FakeClock(FixedUtcNow), Microsoft.Extensions.Logging.Abstractions.NullLogger<InterviewTaskEffectsService>.Instance));

    private static OfferCandidateHandler OfferHandler(RecruitmentDbContext db) =>
        OfferHandlerFactory.Create(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader(),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            new FakeCompanyRecruitmentSettingsReader(), new FakeAuditPublisher());

    private static RecordInterviewOutcomeHandler RecordHandler(RecruitmentDbContext db) =>
        new(OutcomeWiring.Recorder(db, new FakeAuditPublisher()),
            new InterviewOutcomeTaskReconciliationService(db, new FakeTaskResolution(new FakeTaskCompleter(), new FakeTaskCanceller()), OutcomeWiring.Delivery(db, new FakeAuditPublisher()), new FakeClock(FixedUtcNow), Microsoft.Extensions.Logging.Abstractions.NullLogger<InterviewOutcomeTaskReconciliationService>.Instance));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
