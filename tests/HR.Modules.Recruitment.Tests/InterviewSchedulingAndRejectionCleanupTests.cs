using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RejectCandidate;
using HR.Modules.Recruitment.Features.ScheduleInterview;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

public class InterviewSchedulingAndRejectionCleanupTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private sealed record Seed(
        Guid CompanyId,
        Vacancy Vacancy,
        Application Application,
        RecruitmentStage Applied,
        RecruitmentStage First,
        RecruitmentStage Assessment,
        RecruitmentStage Second,
        RecruitmentStage Offer);

    private static async Task<Seed> SeedAsync(
        RecruitmentDbContext db,
        Func<RecruitmentStage, RecruitmentStage, RecruitmentStage, RecruitmentStage, RecruitmentStage> startStage,
        bool secondInterviewStage = true,
        Guid? companyId = null)
    {
        companyId ??= Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId.Value, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var applied = RecruitmentStage.Create(Guid.NewGuid(), companyId.Value, "Applied", 1, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.NewApplication);
        var first = RecruitmentStage.Create(Guid.NewGuid(), companyId.Value, "First Interview", 2, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        var assessment = RecruitmentStage.Create(Guid.NewGuid(), companyId.Value, "Assessment", 3, false, RecruitmentStageTerminalOutcome.None, Now);
        var second = RecruitmentStage.Create(Guid.NewGuid(), companyId.Value, "Second Interview", 4, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        if (!secondInterviewStage)
            second.SetActiveStatus(false, Now);
        var offer = RecruitmentStage.Create(Guid.NewGuid(), companyId.Value, "Offer", 5, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);
        var hired = RecruitmentStage.Create(Guid.NewGuid(), companyId.Value, "Hired", 6, true, RecruitmentStageTerminalOutcome.Hired, Now);
        var rejected = RecruitmentStage.Create(Guid.NewGuid(), companyId.Value, "Rejected", 7, true, RecruitmentStageTerminalOutcome.Rejected, Now);
        db.RecruitmentStages.AddRange(applied, first, assessment, second, offer, hired, rejected);

        var candidate = Candidate.Create(Guid.NewGuid(), companyId.Value, "Emma", "Clarke", $"{Guid.NewGuid():N}@example.com", null, Now);
        var application = Application.Create(
            Guid.NewGuid(), companyId.Value, vacancy.Id, candidate.Id, startStage(applied, first, assessment, second).Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        return new Seed(companyId.Value, vacancy, application, applied, first, assessment, second, offer);
    }

    private static Interview AddInterview(
        RecruitmentDbContext db, Seed seed, RecruitmentStage stage, InterviewOutcome outcome, int dayOffset = 0)
    {
        var interview = Interview.Create(
            Guid.NewGuid(), seed.CompanyId, seed.Application.Id, Guid.NewGuid(), Now.AddDays(dayOffset), 30, null, Now, stage.Id);
        if (outcome == InterviewOutcome.Cancelled) interview.Cancel(Now);
        else if (outcome != InterviewOutcome.Pending) interview.RecordOutcome(outcome, null, Now);

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

    private static RejectCandidateRequest RejectRequest(Seed seed, Guid? idempotencyKey = null) => new()
    {
        CompanyId = seed.CompanyId,
        VacancyId = seed.Vacancy.Id,
        ApplicationId = seed.Application.Id,
        RejectionReason = "No",
        IdempotencyKey = idempotencyKey?.ToString(),
    };

    [Fact]
    public async Task Schedule_From_Applied_Moves_To_First_Interview_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (applied, _, _, _) => applied);
        var events = new FakeIntegrationEventPublisher();

        var result = await ScheduleHandler(db, events).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.First.Id, (await db.Interviews.SingleAsync()).StageId);
        Assert.Equal(seed.First.Id, (await db.Applications.SingleAsync()).CurrentStageId);
        Assert.Single(await db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Single(events.PublishedEvents);
    }

    [Fact]
    public async Task Schedule_From_Custom_Stage_Between_Interview_Stages_Moves_To_Second_Interview_Stage()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, assessment, _) => assessment);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -5);
        await db.SaveChangesAsync();
        var events = new FakeIntegrationEventPublisher();

        var result = await ScheduleHandler(db, events).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var created = await db.Interviews.SingleAsync(i => i.Outcome == InterviewOutcome.Pending);
        Assert.Equal(seed.Second.Id, created.StageId);
        Assert.Equal(seed.Second.Id, (await db.Applications.SingleAsync()).CurrentStageId);
        var history = await db.ApplicationStageHistoryEntries.SingleAsync();
        Assert.Equal(seed.Assessment.Id, history.PreviousStageId);
        Assert.Equal(seed.Second.Id, history.NewStageId);
        Assert.Single(events.PublishedEvents);
    }

    [Fact]
    public async Task Schedule_In_Current_Interview_Stage_Associates_With_Current_Stage_Without_Moving()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var events = new FakeIntegrationEventPublisher();

        var result = await ScheduleHandler(db, events).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.First.Id, (await db.Interviews.SingleAsync()).StageId);
        Assert.Empty(await db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Empty(events.PublishedEvents);
    }

    [Fact]
    public async Task Schedule_From_Custom_Stage_After_Final_Interview_Stage_Is_Rejected_Without_Creating_Interview()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, _, assessment, _) => assessment, secondInterviewStage: false);
        AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -5);
        await db.SaveChangesAsync();

        var result = await ScheduleHandler(db).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Single(await db.Interviews.ToListAsync());
        Assert.Equal(seed.Assessment.Id, (await db.Applications.SingleAsync()).CurrentStageId);
        Assert.Empty(await db.ApplicationStageHistoryEntries.ToListAsync());
    }

    [Fact]
    public async Task Schedule_When_No_Interview_Stage_Is_Configured_Is_Rejected()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (applied, _, _, _) => applied);
        foreach (var stage in await db.RecruitmentStages.Where(s => s.Purpose == RecruitmentStagePurpose.Interview).ToListAsync())
            stage.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var result = await ScheduleHandler(db).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.Interviews.ToListAsync());
    }

    [Fact]
    public async Task Every_Scheduled_Interview_Has_A_Stage_Association()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (applied, _, _, _) => applied);

        Assert.True((await ScheduleHandler(db).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None)).IsSuccess);
        var first = await db.Interviews.SingleAsync();
        first.RecordOutcome(InterviewOutcome.Passed, null, Now);
        var application = await db.Applications.SingleAsync();
        application.MoveToStage(seed.Assessment.Id, Now);
        await db.SaveChangesAsync();

        Assert.True((await ScheduleHandler(db).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None)).IsSuccess);

        Assert.All(await db.Interviews.ToListAsync(), i => Assert.NotNull(i.StageId));
    }

    [Fact]
    public async Task Reject_Cancels_All_Pending_Interviews_And_Both_Task_Types_Recording_A_Completed_Cleanup()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var completed = AddInterview(db, seed, seed.First, InterviewOutcome.Passed, -3);
        var pendingA = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 1);
        var pendingB = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 2);
        seed.Application.SetInterviewOutcome(InterviewOutcome.Pending, Now);
        await db.SaveChangesAsync();
        var canceller = new FakeTaskCanceller();

        var result = await RejectHandler(db, canceller).HandleAsync(RejectRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var interviews = await db.Interviews.ToListAsync();
        Assert.Equal(InterviewOutcome.Passed, interviews.Single(i => i.Id == completed.Id).Outcome);
        Assert.Equal(InterviewOutcome.Cancelled, interviews.Single(i => i.Id == pendingA.Id).Outcome);
        Assert.Equal(InterviewOutcome.Cancelled, interviews.Single(i => i.Id == pendingB.Id).Outcome);
        Assert.Equal(InterviewOutcome.Cancelled, (await db.Applications.SingleAsync()).InterviewOutcome);
        Assert.Contains(canceller.Calls, c => c.ActionType == TaskActionType.Review);
        Assert.Contains(canceller.Calls, c => c.ActionType == TaskActionType.Complete);
        Assert.All(canceller.Calls, c => Assert.Equal(new[] { pendingA.Id, pendingB.Id }.Order(), c.SourceEntityIds.Order()));
        Assert.NotNull((await db.InterviewTaskCleanups.SingleAsync()).CompletedAt);
    }

    [Fact]
    public async Task Reject_Without_Pending_Interviews_Records_No_Cleanup()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (applied, _, _, _) => applied);

        var result = await RejectHandler(db, new FakeTaskCanceller()).HandleAsync(RejectRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.InterviewTaskCleanups.ToListAsync());
    }

    [Fact]
    public async Task Tasks_Failure_After_Recruitment_Commit_Keeps_Rejection_And_Reconciliation_Completes_Cleanup()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var pending = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 1);
        await db.SaveChangesAsync();
        var events = new FakeIntegrationEventPublisher();
        var canceller = new FakeTaskCanceller { Fail = true };

        var result = await RejectHandler(db, canceller, events).HandleAsync(RejectRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(events.PublishedEvents);
        Assert.Equal(InterviewOutcome.Cancelled, (await db.Interviews.SingleAsync()).Outcome);
        var outstanding = await db.InterviewTaskCleanups.SingleAsync();
        Assert.Null(outstanding.CompletedAt);
        Assert.Equal(1, outstanding.AttemptCount);
        Assert.NotNull(outstanding.FailureReason);

        canceller.Fail = false;
        var processed = await CleanupService(db, canceller).RunAllOutstandingAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.NotNull((await db.InterviewTaskCleanups.SingleAsync()).CompletedAt);
        Assert.Equal(2, canceller.Calls.Count);
        Assert.All(canceller.Calls, c => Assert.Equal(new[] { pending.Id }, c.SourceEntityIds));
        Assert.Single(events.PublishedEvents);
        Assert.Single(await db.ApplicationStageHistoryEntries.ToListAsync());
    }

    [Fact]
    public async Task Replaying_Idempotent_Rejection_While_Cleanup_Outstanding_Retries_Cleanup()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 1);
        await db.SaveChangesAsync();
        var events = new FakeIntegrationEventPublisher();
        var canceller = new FakeTaskCanceller { Fail = true };
        var key = Guid.NewGuid();

        var first = await RejectHandler(db, canceller, events).HandleAsync(RejectRequest(seed, key), Guid.NewGuid(), CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Null((await db.InterviewTaskCleanups.SingleAsync()).CompletedAt);

        canceller.Fail = false;
        var replay = await RejectHandler(db, canceller, events).HandleAsync(RejectRequest(seed, key), Guid.NewGuid(), CancellationToken.None);

        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value!.Id, replay.Value!.Id);
        Assert.NotNull((await db.InterviewTaskCleanups.SingleAsync()).CompletedAt);
        Assert.Equal(2, canceller.Calls.Count);
        Assert.Single(events.PublishedEvents);
        Assert.Single(await db.ApplicationStageHistoryEntries.ToListAsync());
    }

    [Fact]
    public async Task Rerunning_Completed_Cleanup_Does_Nothing_And_Rerunning_Failed_Cleanup_Is_Safe()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 1);
        await db.SaveChangesAsync();
        var canceller = new FakeTaskCanceller();

        await RejectHandler(db, canceller).HandleAsync(RejectRequest(seed), Guid.NewGuid(), CancellationToken.None);
        var callsAfterReject = canceller.Calls.Count;

        var processed = await CleanupService(db, canceller).RunAllOutstandingAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Equal(callsAfterReject, canceller.Calls.Count);

        var cleanup = await db.InterviewTaskCleanups.SingleAsync();
        Assert.True(await CleanupService(db, canceller).RunAsync(cleanup, CancellationToken.None));
        Assert.True(await CleanupService(db, canceller).RunAsync(cleanup, CancellationToken.None));
    }

    [Fact]
    public async Task Cleanup_Is_Scoped_To_Company_Application_And_Own_Interviews()
    {
        await using var db = BuildContext();
        var seed = await SeedAsync(db, (_, first, _, _) => first);
        var other = await SeedAsync(db, (_, first, _, _) => first);
        var mine = AddInterview(db, seed, seed.First, InterviewOutcome.Pending, 1);
        var theirs = AddInterview(db, other, other.First, InterviewOutcome.Pending, 1);
        await db.SaveChangesAsync();
        var canceller = new FakeTaskCanceller();

        var result = await RejectHandler(db, canceller).HandleAsync(RejectRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(canceller.Calls, c =>
        {
            Assert.Equal(seed.CompanyId, c.CompanyId);
            Assert.Equal(new[] { mine.Id }, c.SourceEntityIds);
        });
        Assert.Equal(InterviewOutcome.Pending, (await db.Interviews.SingleAsync(i => i.Id == theirs.Id)).Outcome);
        Assert.Equal(other.First.Id, (await db.Applications.SingleAsync(a => a.Id == other.Application.Id)).CurrentStageId);
        Assert.Equal(seed.CompanyId, (await db.InterviewTaskCleanups.SingleAsync()).CompanyId);
    }

    private static InterviewTaskCleanupService CleanupService(RecruitmentDbContext db, FakeTaskCanceller canceller) =>
        new(db, canceller, new FakeClock(FixedUtcNow), NullLogger<InterviewTaskCleanupService>.Instance);

    private static RejectCandidateHandler RejectHandler(
        RecruitmentDbContext db, FakeTaskCanceller canceller, FakeIntegrationEventPublisher? events = null) =>
        new(db, new FakeClock(FixedUtcNow),
            new RecruitmentStageChangeRecorder(db, events ?? new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            CleanupService(db, canceller));

    private static ScheduleInterviewHandler ScheduleHandler(RecruitmentDbContext db, FakeIntegrationEventPublisher? events = null) =>
        new(db, new FakeNotificationWriter(), new FakeClock(FixedUtcNow), new FakePositionProfileReader(),
            new RecruitmentStageChangeRecorder(db, events ?? new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            new InterviewTaskEffectsService(db, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeTaskCompleter(), new FakeClock(FixedUtcNow), Microsoft.Extensions.Logging.Abstractions.NullLogger<InterviewTaskEffectsService>.Instance));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
