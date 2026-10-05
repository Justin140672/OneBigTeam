using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListApplicationsForVacancy;
using HR.Modules.Recruitment.Features.MoveApplicationStage;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Features.RejectCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.Logging.Abstractions;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class InterviewStageEnforcementTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private sealed record Seed(
        Guid CompanyId, Vacancy Vacancy, Application Application,
        RecruitmentStage Cv, RecruitmentStage First, RecruitmentStage Second, RecruitmentStage Offer);

    private static RecruitmentStage Stage(Guid companyId, string name, int order, RecruitmentStagePurpose purpose) =>
        RecruitmentStage.Create(Guid.NewGuid(), companyId, name, order, false, RecruitmentStageTerminalOutcome.None, Now, purpose);

    private static async Task<Seed> SeedAsync(
        RecruitmentDbContext db, Func<RecruitmentStage, RecruitmentStage, RecruitmentStage, RecruitmentStage, RecruitmentStage> start)
    {
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var cv = Stage(companyId, "CV Review", 2, RecruitmentStagePurpose.CvReview);
        var first = Stage(companyId, "First Interview", 3, RecruitmentStagePurpose.Interview);
        var second = Stage(companyId, "Second Interview", 4, RecruitmentStagePurpose.Interview);
        var offer = Stage(companyId, "Offer", 5, RecruitmentStagePurpose.Offer);
        var hired = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Hired", 6, true, RecruitmentStageTerminalOutcome.Hired, Now);
        var rejected = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected", 7, true, RecruitmentStageTerminalOutcome.Rejected, Now);
        db.RecruitmentStages.AddRange(cv, first, second, offer, hired, rejected);

        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, start(cv, first, second, offer).Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return new Seed(companyId, vacancy, application, cv, first, second, offer);
    }

    private static Interview AddInterview(
        RecruitmentDbContext db, Seed seed, RecruitmentStage? stage, InterviewOutcome outcome, int dayOffset = 0)
    {
        var interview = Interview.Create(
            Guid.NewGuid(), seed.CompanyId, seed.Application.Id, Guid.NewGuid(), Now.AddDays(dayOffset), 30, null, Now, stage?.Id);
        if (outcome == InterviewOutcome.Cancelled) interview.Cancel(Now);
        else if (outcome != InterviewOutcome.Pending) interview.RecordOutcome(outcome, null, Now);
        db.Interviews.Add(interview);
        return interview;
    }

    private static List<RecruitmentStage> Stages(Seed s) => [s.Cv, s.First, s.Second, s.Offer];

    [Fact]
    public async Task Evaluate_Second_Stage_Without_Interview_Is_Not_Passed_Even_When_First_Passed()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        var first = AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);

        var state = InterviewStageWorkflow.Evaluate(seed.Second, Stages(seed), [first]);

        Assert.True(state.IsInterviewStage);
        Assert.Null(state.LatestOutcome);
        Assert.False(state.CurrentStageHasPendingInterview);
        Assert.False(state.HasNextInterviewStage);
        Assert.False(state.AllRequiredInterviewStagesPassed);
    }

    [Fact]
    public async Task Evaluate_First_Passed_With_Second_Ahead_Exposes_Next_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var interview = AddInterview(db, seed, seed.First, InterviewOutcome.Passed);

        var state = InterviewStageWorkflow.Evaluate(seed.First, Stages(seed), [interview]);

        Assert.Equal(InterviewOutcome.Passed, state.LatestOutcome);
        Assert.True(state.HasNextInterviewStage);
        Assert.Equal(seed.Second.Id, state.NextInterviewStageId);
        Assert.Equal("Second Interview", state.NextInterviewStageName);
        Assert.False(state.AllRequiredInterviewStagesPassed);
    }

    [Fact]
    public async Task Evaluate_Final_Stage_Passed_Marks_All_Required_Passed()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        var a = AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);
        var b = AddInterview(db, seed, seed.Second, InterviewOutcome.Passed);

        var state = InterviewStageWorkflow.Evaluate(seed.Second, Stages(seed), [a, b]);

        Assert.True(state.AllRequiredInterviewStagesPassed);
        Assert.False(state.HasNextInterviewStage);
        Assert.Null(InterviewStageWorkflow.DescribeOfferViolation(Stages(seed), [a, b]));
    }

    [Fact]
    public async Task Evaluate_Multiple_Pending_Reports_Earliest_Pending()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var later = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 5);
        var earlier = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 2);

        var state = InterviewStageWorkflow.Evaluate(seed.First, Stages(seed), [later, earlier]);

        Assert.True(state.CurrentStageHasPendingInterview);
        Assert.Equal(earlier.Id, state.PendingInterviewId);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("NoShow")]
    [InlineData("Cancelled")]
    public async Task Offer_Is_Blocked_When_Final_Stage_Latest_Outcome_Is_Not_Passed(string outcomeName)
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        var a = AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);
        var b = AddInterview(db, seed, seed.Second, Enum.Parse<InterviewOutcome>(outcomeName));

        Assert.NotNull(InterviewStageWorkflow.DescribeOfferViolation(Stages(seed), [a, b]));
    }

    [Fact]
    public async Task Offer_Is_Blocked_By_Legacy_Interview_Without_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        var legacy = AddInterview(db, seed, null, InterviewOutcome.Passed, -3);

        Assert.NotNull(InterviewStageWorkflow.DescribeOfferViolation(Stages(seed), [legacy]));
    }

    [Fact]
    public async Task Offer_Is_Allowed_With_No_Interview_Stages_Configured()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (cv, _, _, _) => cv);

        Assert.Null(InterviewStageWorkflow.DescribeOfferViolation([seed.Cv, seed.Offer], []));
    }

    [Fact]
    public async Task Move_Violations_Cover_Skip_Offer_Pending_And_Backward_Correction()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var passedFirst = AddInterview(db, seed, seed.First, InterviewOutcome.Passed);
        var stages = Stages(seed);

        Assert.NotNull(InterviewStageWorkflow.DescribeMoveViolation(seed.Cv, seed.Second, stages, []));
        Assert.NotNull(InterviewStageWorkflow.DescribeMoveViolation(seed.First, seed.Offer, stages, [passedFirst]));
        Assert.Null(InterviewStageWorkflow.DescribeMoveViolation(seed.First, seed.Second, stages, [passedFirst]));
        Assert.Null(InterviewStageWorkflow.DescribeMoveViolation(seed.Second, seed.First, stages, [passedFirst]));
        Assert.Null(InterviewStageWorkflow.DescribeMoveViolation(seed.Cv, seed.First, stages, []));

        var pending = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 3);
        Assert.NotNull(InterviewStageWorkflow.DescribeMoveViolation(seed.First, seed.Cv, stages, [passedFirst, pending]));
    }

    [Fact]
    public async Task MoveStage_Handler_Blocks_First_To_Second_Until_First_Passed_Then_Allows()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);

        var blocked = await MoveHandler(db).HandleAsync(MoveRequest(seed, seed.Second), Guid.NewGuid(), CancellationToken.None);
        Assert.True(blocked.IsFailure);
        Assert.Equal("validation", blocked.Error.Code);
        Assert.Equal(seed.First.Id, (await db.Applications.SingleAsync()).CurrentStageId);

        AddInterview(db, seed, seed.First, InterviewOutcome.Passed);
        await db.SaveChangesAsync();

        var allowed = await MoveHandler(db).HandleAsync(MoveRequest(seed, seed.Second), Guid.NewGuid(), CancellationToken.None);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task MoveStage_Handler_Blocks_Direct_Move_To_Offer_And_Skipping_Stages()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (cv, _, _, _) => cv);

        Assert.True((await MoveHandler(db).HandleAsync(MoveRequest(seed, seed.Offer), Guid.NewGuid(), CancellationToken.None)).IsFailure);
        Assert.True((await MoveHandler(db).HandleAsync(MoveRequest(seed, seed.Second), Guid.NewGuid(), CancellationToken.None)).IsFailure);
        Assert.Equal(seed.Cv.Id, (await db.Applications.SingleAsync()).CurrentStageId);
    }

    [Fact]
    public async Task MoveStage_Handler_Blocks_Moving_Away_With_Pending_Interview()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Pending);
        await db.SaveChangesAsync();

        var result = await MoveHandler(db).HandleAsync(MoveRequest(seed, seed.Cv), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task MoveStage_Handler_Still_Rejects_Terminal_Target()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var hired = await db.RecruitmentStages.SingleAsync(s => s.Name == "Hired");

        var result = await MoveHandler(db).HandleAsync(MoveRequest(seed, hired), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Offer_Handler_Blocks_Application_Manually_Placed_In_Offer_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, _, offer) => offer);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);
        await db.SaveChangesAsync();

        var result = await OfferHandler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id, ApplicationId = seed.Application.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task Offer_Handler_Succeeds_After_Final_Stage_Passed_From_Offer_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, _, offer) => offer);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);
        AddInterview(db, seed, seed.Second, InterviewOutcome.Passed, -1);
        await db.SaveChangesAsync();

        var result = await OfferHandler(db).HandleAsync(
            new OfferCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id, ApplicationId = seed.Application.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Reject_Cancels_Pending_Interviews_And_Their_Tasks_And_Keeps_Completed_History()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        var completed = AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);
        var pendingA = AddInterview(db, seed, seed.Second, InterviewOutcome.Pending, 1);
        var pendingB = AddInterview(db, seed, seed.Second, InterviewOutcome.Pending, 2);
        await db.SaveChangesAsync();
        var canceller = new FakeTaskCanceller();

        var result = await RejectHandler(db, canceller).HandleAsync(
            new RejectCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id, ApplicationId = seed.Application.Id, RejectionReason = "No" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        db.ChangeTracker.Clear();
        var interviews = await db.Interviews.ToListAsync();
        Assert.Equal(InterviewOutcome.Passed, interviews.Single(i => i.Id == completed.Id).Outcome);
        Assert.Equal(InterviewOutcome.Cancelled, interviews.Single(i => i.Id == pendingA.Id).Outcome);
        Assert.Equal(InterviewOutcome.Cancelled, interviews.Single(i => i.Id == pendingB.Id).Outcome);
        Assert.Equal(2, canceller.Calls.Count);
        Assert.All(canceller.Calls, c => Assert.Equal(new[] { pendingA.Id, pendingB.Id }.Order(), c.SourceEntityIds.Order()));
        Assert.Contains(canceller.Calls, c => c.ActionType == TaskActionType.Review);
        Assert.Contains(canceller.Calls, c => c.ActionType == TaskActionType.Complete);
        Assert.Single(await db.ApplicationStageHistoryEntries.ToListAsync());
    }

    [Fact]
    public async Task Reminder_Jobs_Ignore_Interviews_Cancelled_By_Rejection()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 0);
        AddInterview(db, seed, seed.First, InterviewOutcome.Pending, -3);
        await db.SaveChangesAsync();

        var rejected = await RejectHandler(db, new FakeTaskCanceller()).HandleAsync(
            new RejectCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id, ApplicationId = seed.Application.Id },
            Guid.NewGuid(), CancellationToken.None);
        Assert.True(rejected.IsSuccess);

        var writer = new FakeNotificationWriter();
        await new HR.Modules.Recruitment.Jobs.InterviewReminderJob(db, writer, new FakeClock(FixedUtcNow.AddMinutes(-30))).ExecuteAsync();
        await new HR.Modules.Recruitment.Jobs.OutstandingInterviewFeedbackReminderJob(db, writer, new FakeClock(FixedUtcNow.AddDays(10))).ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task RecordOutcome_On_Legacy_Interview_Without_Stage_Assigns_Current_Interview_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var legacy = AddInterview(db, seed, null, InterviewOutcome.Pending);
        await db.SaveChangesAsync();

        var result = await OutcomeWiring.Recorder(db, new FakeAuditPublisher())
            .RecordAsync(new HR.Modules.Recruitment.Features.RecordInterviewOutcome.RecordInterviewOutcomeRequest
            {
                CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id, ApplicationId = seed.Application.Id,
                InterviewId = legacy.Id, Outcome = InterviewOutcome.Passed,
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.First.Id, (await db.Interviews.SingleAsync()).StageId);
    }

    [Fact]
    public async Task Reject_With_Only_Completed_Interviews_Does_Not_Touch_Tasks()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Failed);
        await db.SaveChangesAsync();
        var canceller = new FakeTaskCanceller();

        var result = await RejectHandler(db, canceller).HandleAsync(
            new RejectCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id, ApplicationId = seed.Application.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(canceller.Calls);
        Assert.Equal(InterviewOutcome.Failed, (await db.Interviews.SingleAsync()).Outcome);
    }

    [Fact]
    public async Task ListApplications_Returns_Stage_Specific_State_Not_Application_Wide_Outcome()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, second, _) => second);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);
        seed.Application.SetInterviewOutcome(InterviewOutcome.Passed, Now);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id }, CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(InterviewOutcome.Passed, item.InterviewOutcome);
        Assert.Null(item.CurrentStageInterviewOutcome);
        Assert.False(item.CurrentStageHasPendingInterview);
        Assert.False(item.AllRequiredInterviewStagesPassed);
    }

    [Fact]
    public async Task ListApplications_Exposes_Pending_Interview_And_Next_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var pending = AddInterview(db, seed, seed.First, InterviewOutcome.Pending);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id }, CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.True(item.CurrentStageHasPendingInterview);
        Assert.Equal(pending.Id, item.PendingInterviewId);
        Assert.True(item.HasNextInterviewStage);
        Assert.Equal(seed.Second.Id, item.NextInterviewStageId);
    }

    private static MoveApplicationStageRequest MoveRequest(Seed seed, RecruitmentStage target) => new()
    {
        CompanyId = seed.CompanyId, VacancyId = seed.Vacancy.Id, ApplicationId = seed.Application.Id, NewStageId = target.Id,
    };

    private static MoveApplicationStageHandler MoveHandler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()));

    private static OfferCandidateHandler OfferHandler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader(),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            new FakeCompanyRecruitmentSettingsReader(), new FakeAuditPublisher());

    private static RejectCandidateHandler RejectHandler(RecruitmentDbContext db, FakeTaskCanceller canceller) =>
        new(db, new FakeClock(FixedUtcNow),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            new InterviewTaskCleanupService(db, canceller, new FakeClock(FixedUtcNow), NullLogger<InterviewTaskCleanupService>.Instance));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
