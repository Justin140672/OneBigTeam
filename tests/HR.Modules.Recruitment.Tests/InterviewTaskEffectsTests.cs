using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RejectCandidate;
using HR.Modules.Recruitment.Features.ScheduleInterview;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

public class InterviewTaskEffectsTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private sealed record Seed(Guid CompanyId, Guid VacancyId, Guid ApplicationId);

    private static RecruitmentDbContext BuildContext(string name) =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(name).Options);

    private static InterviewTaskEffectsService EffectsService(
        RecruitmentDbContext db, FakeTaskCreator creator, FakeTaskCanceller canceller, FakeTaskCompleter? completer = null) =>
        new(db, creator, canceller, completer ?? new FakeTaskCompleter(), new FakeClock(FixedUtcNow), NullLogger<InterviewTaskEffectsService>.Instance);

    private static ScheduleInterviewHandler ScheduleHandler(
        RecruitmentDbContext db, FakeTaskCreator creator, FakeTaskCanceller canceller, FakeTaskCompleter? completer = null) =>
        new(db, new FakeNotificationWriter(), new FakeClock(FixedUtcNow), new FakePositionProfileReader(),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            EffectsService(db, creator, canceller, completer));

    private static async Task<Seed> SeedAsync(string dbName)
    {
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var applied = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Applied", 1, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.NewApplication);
        var first = RecruitmentStage.Create(Guid.NewGuid(), companyId, "First Interview", 2, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        var rejected = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected", 3, true, RecruitmentStageTerminalOutcome.Rejected, Now);
        db.RecruitmentStages.AddRange(applied, first, rejected);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"{Guid.NewGuid():N}@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, applied.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return new Seed(companyId, vacancy.Id, application.Id);
    }

    private static ScheduleInterviewRequest ScheduleRequest(Seed seed) => new()
    {
        CompanyId = seed.CompanyId,
        VacancyId = seed.VacancyId,
        ApplicationId = seed.ApplicationId,
        InterviewerEmployeeId = Guid.NewGuid(),
        ScheduledAt = Now.AddDays(3),
    };

    private static async Task RejectFromOtherContextAsync(string dbName, Seed seed)
    {
        await using var other = BuildContext(dbName);
        var handler = new RejectCandidateHandler(other, new FakeClock(FixedUtcNow),
            new RecruitmentStageChangeRecorder(other, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            new InterviewTaskCleanupService(other, new FakeTaskCanceller(), new FakeClock(FixedUtcNow), NullLogger<InterviewTaskCleanupService>.Instance));
        var result = await handler.HandleAsync(
            new RejectCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, ApplicationId = seed.ApplicationId },
            Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    private static Task<Result<ScheduleInterviewResponse>> ScheduleAsync(
        RecruitmentDbContext db, Seed seed, FakeTaskCreator creator, FakeTaskCanceller canceller) =>
        ScheduleHandler(db, creator, canceller).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

    private static Task<List<InterviewTaskEffect>> OutstandingAsync(RecruitmentDbContext db) =>
        db.InterviewTaskEffects.Where(e => e.CompletedAt == null).ToListAsync();

    [Fact]
    public async Task Scheduling_Writes_Effect_Row_And_Creates_Tasks_Idempotently()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var creator = new FakeTaskCreator();
        await using var db = BuildContext(dbName);

        Assert.True((await ScheduleAsync(db, seed, creator, new FakeTaskCanceller())).IsSuccess);

        var effect = await db.InterviewTaskEffects.SingleAsync();
        Assert.NotNull(effect.CompletedAt);
        Assert.Equal(2, creator.Created.Count);

        Assert.True(await EffectsService(db, creator, new FakeTaskCanceller()).RunAsync(effect, CancellationToken.None));
        Assert.Equal(2, creator.Created.Count);
    }

    [Fact]
    public async Task Task_Creation_Throwing_After_First_Task_Persisted_Is_Reconciled_Without_Duplicates()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var creator = new FakeTaskCreator { AfterCreate = n => throw new InvalidOperationException("boom") };
        await using var db = BuildContext(dbName);

        Assert.True((await ScheduleAsync(db, seed, creator, new FakeTaskCanceller())).IsSuccess);

        Assert.Single(creator.Created);
        Assert.Single(await OutstandingAsync(db));

        creator.AfterCreate = null;
        Assert.Equal(1, await EffectsService(db, creator, new FakeTaskCanceller()).RunAllOutstandingAsync(CancellationToken.None));

        Assert.Equal(2, creator.Created.Count);
        Assert.Equal(2, creator.Created.Select(c => c.ActionType).Distinct().Count());
        Assert.Empty(await OutstandingAsync(db));
    }

    [Fact]
    public async Task Process_Stopping_After_Rejection_Mid_Creation_Is_Reconciled_To_Cancelled_Tasks_By_Job()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var creator = new FakeTaskCreator
        {
            AfterCreate = async _ =>
            {
                await RejectFromOtherContextAsync(dbName, seed);
                throw new OperationCanceledException();
            },
        };
        await using var db = BuildContext(dbName);

        await Assert.ThrowsAsync<OperationCanceledException>(() => ScheduleAsync(db, seed, creator, new FakeTaskCanceller()));

        await using var job = BuildContext(dbName);
        Assert.Single(await OutstandingAsync(job));
        var canceller = new FakeTaskCanceller();

        await EffectsService(job, creator, canceller).RunAllOutstandingAsync(CancellationToken.None);

        var interview = await job.Interviews.SingleAsync();
        Assert.Equal(InterviewOutcome.Cancelled, interview.Outcome);
        Assert.Equal(2, canceller.Calls.Count);
        Assert.All(canceller.Calls, c => Assert.Equal(new[] { interview.Id }, c.SourceEntityIds));
        Assert.Single(creator.Created);
        Assert.Empty(await OutstandingAsync(job));
    }

    [Fact]
    public async Task Rejection_Between_Commit_And_Creation_Job_Only_Path_Creates_No_Tasks_And_Cancels()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var creator = new FakeTaskCreator { FailBeforePersist = true };
        await using var db = BuildContext(dbName);

        Assert.True((await ScheduleAsync(db, seed, creator, new FakeTaskCanceller())).IsSuccess);
        await RejectFromOtherContextAsync(dbName, seed);

        creator.FailBeforePersist = false;
        await using var job = BuildContext(dbName);
        var canceller = new FakeTaskCanceller();
        await EffectsService(job, creator, canceller).RunAllOutstandingAsync(CancellationToken.None);

        Assert.Empty(creator.Created);
        Assert.Equal(2, canceller.Calls.Count);
        Assert.Empty(await OutstandingAsync(job));
    }

    [Fact]
    public async Task Rejection_Mid_Creation_With_Inline_Completion_Cancels_Created_Tasks()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var creator = new FakeTaskCreator { AfterCreate = n => n == 1 ? RejectFromOtherContextAsync(dbName, seed) : Task.CompletedTask };
        var canceller = new FakeTaskCanceller();
        await using var db = BuildContext(dbName);

        Assert.True((await ScheduleAsync(db, seed, creator, canceller)).IsSuccess);

        Assert.Equal(2, creator.Created.Count);
        Assert.Equal(2, canceller.Calls.Count);
        Assert.Empty(await OutstandingAsync(db));
    }

    [Fact]
    public async Task Effects_Reconciliation_Is_Scoped_To_Company()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var creator = new FakeTaskCreator { FailBeforePersist = true };
        await using var db = BuildContext(dbName);
        await ScheduleAsync(db, seed, creator, new FakeTaskCanceller());
        creator.FailBeforePersist = false;

        var processed = await EffectsService(db, creator, new FakeTaskCanceller())
            .RunOutstandingForApplicationAsync(Guid.NewGuid(), seed.ApplicationId, CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Empty(creator.Created);
        Assert.Single(await OutstandingAsync(db));
    }

    private static async Task RecordOutcomeFromOtherContextAsync(string dbName, Seed seed, InterviewOutcome outcome)
    {
        await using var other = BuildContext(dbName);
        var interview = await other.Interviews.SingleAsync(i => i.ApplicationId == seed.ApplicationId);
        interview.RecordOutcome(outcome, null, Now);
        await other.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("Failed")]
    [InlineData("NoShow")]
    public async Task Outcome_Recorded_Between_Task_Creations_Completes_The_Feedback_Task(string outcomeName)
    {
        var outcome = Enum.Parse<InterviewOutcome>(outcomeName);
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var creator = new FakeTaskCreator { AfterCreate = n => n == 1 ? RecordOutcomeFromOtherContextAsync(dbName, seed, outcome) : Task.CompletedTask };
        var canceller = new FakeTaskCanceller();
        var completer = new FakeTaskCompleter();
        await using var db = BuildContext(dbName);

        var result = await ScheduleHandler(db, creator, canceller, completer).HandleAsync(ScheduleRequest(seed), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, creator.Created.Count);
        var call = Assert.Single(completer.Calls);
        Assert.Equal(seed.CompanyId, call.CompanyId);
        Assert.Equal(TaskActionType.Complete, call.ActionType);
        Assert.Equal(TaskActionType.Review, Assert.Single(canceller.Calls).ActionType);
        Assert.Empty(await OutstandingAsync(db));
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("Failed")]
    [InlineData("NoShow")]
    public async Task Outcome_Recorded_Before_Creation_Job_Only_Path_Creates_Nothing_And_Completes_Feedback_Task_Idempotently(string outcomeName)
    {
        var outcome = Enum.Parse<InterviewOutcome>(outcomeName);
        var dbName = Guid.NewGuid().ToString("N");
        var seed = await SeedAsync(dbName);
        var creator = new FakeTaskCreator { FailBeforePersist = true };
        await using var db = BuildContext(dbName);
        Assert.True((await ScheduleAsync(db, seed, creator, new FakeTaskCanceller())).IsSuccess);
        await RecordOutcomeFromOtherContextAsync(dbName, seed, outcome);

        creator.FailBeforePersist = false;
        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();
        await using var job = BuildContext(dbName);
        var service = EffectsService(job, creator, canceller, completer);
        await service.RunAllOutstandingAsync(CancellationToken.None);
        var effect = await job.InterviewTaskEffects.SingleAsync();
        await service.RunAsync(effect, CancellationToken.None);

        Assert.Empty(creator.Created);
        Assert.All(canceller.Calls, c => Assert.Equal(TaskActionType.Review, c.ActionType));
        Assert.NotEmpty(completer.Calls);
        Assert.All(completer.Calls, c =>
        {
            Assert.Equal(seed.CompanyId, c.CompanyId);
            Assert.Equal(TaskActionType.Complete, c.ActionType);
        });
        Assert.NotNull(effect.CompletedAt);
    }
}
