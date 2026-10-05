using HR.Modules.Tasks.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RejectCandidate;
using HR.Modules.Recruitment.Features.ScheduleInterview;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

public class InterviewTaskEffectsPostgresTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private async Task<(Guid CompanyId, Guid VacancyId, Guid ApplicationId)> SeedAsync()
    {
        await using var db = fixture.BuildContext();
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
        return (companyId, vacancy.Id, application.Id);
    }

    private InterviewTaskEffectsService Effects(FakeTaskCreator creator, FakeTaskCanceller canceller, HR.Modules.Recruitment.Persistence.RecruitmentDbContext db, FakeTaskCompleter? completer = null) =>
        new(db, creator, canceller, completer ?? new FakeTaskCompleter(), new FakeClock(FixedUtcNow), NullLogger<InterviewTaskEffectsService>.Instance);

    [Fact]
    public async Task Rejection_Mid_Creation_And_Process_Stop_Is_Reconciled_By_Job_On_Postgres()
    {
        var (companyId, vacancyId, applicationId) = await SeedAsync();
        var creator = new FakeTaskCreator
        {
            AfterCreate = async _ =>
            {
                await using var other = fixture.BuildContext();
                var reject = new RejectCandidateHandler(other, new FakeClock(FixedUtcNow),
                    new RecruitmentStageChangeRecorder(other, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
                    new InterviewTaskCleanupService(other, new FakeTaskCanceller(), new FakeClock(FixedUtcNow), NullLogger<InterviewTaskCleanupService>.Instance));
                Assert.True((await reject.HandleAsync(
                    new RejectCandidateRequest { CompanyId = companyId, VacancyId = vacancyId, ApplicationId = applicationId },
                    Guid.NewGuid(), CancellationToken.None)).IsSuccess);
                throw new OperationCanceledException();
            },
        };

        await using (var db = fixture.BuildContext())
        {
            var handler = new ScheduleInterviewHandler(db, new FakeNotificationWriter(), new FakeClock(FixedUtcNow), new FakePositionProfileReader(),
                new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
                Effects(creator, new FakeTaskCanceller(), db));

            await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(
                new ScheduleInterviewRequest
                {
                    CompanyId = companyId,
                    VacancyId = vacancyId,
                    ApplicationId = applicationId,
                    InterviewerEmployeeId = Guid.NewGuid(),
                    ScheduledAt = Now.AddDays(3),
                },
                Guid.NewGuid(), CancellationToken.None));
        }

        await using var job = fixture.BuildContext();
        var effect = await job.InterviewTaskEffects.SingleAsync(e => e.CompanyId == companyId);
        Assert.Null(effect.CompletedAt);
        var canceller = new FakeTaskCanceller();

        await Effects(creator, canceller, job).RunAsync(effect, CancellationToken.None);

        job.ChangeTracker.Clear();
        Assert.NotNull((await job.InterviewTaskEffects.SingleAsync(e => e.Id == effect.Id)).CompletedAt);
        Assert.Equal(InterviewOutcome.Cancelled, (await job.Interviews.SingleAsync(i => i.ApplicationId == applicationId)).Outcome);
        Assert.Equal(2, canceller.Calls.Count);
        Assert.Single(creator.Created);
    }

    [Fact]
    public async Task Outcome_Recorded_Between_Task_Creations_Completes_Feedback_Task_On_Postgres()
    {
        var (companyId, vacancyId, applicationId) = await SeedAsync();
        var creator = new FakeTaskCreator
        {
            AfterCreate = async n =>
            {
                if (n != 1) return;
                await using var other = fixture.BuildContext();
                var interview = await other.Interviews.SingleAsync(i => i.ApplicationId == applicationId);
                interview.RecordOutcome(InterviewOutcome.Passed, null, Now);
                await other.SaveChangesAsync();
            },
        };
        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();

        await using var db = fixture.BuildContext();
        var handler = new ScheduleInterviewHandler(db, new FakeNotificationWriter(), new FakeClock(FixedUtcNow), new FakePositionProfileReader(),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()),
            Effects(creator, canceller, db, completer));

        var result = await handler.HandleAsync(
            new ScheduleInterviewRequest
            {
                CompanyId = companyId,
                VacancyId = vacancyId,
                ApplicationId = applicationId,
                InterviewerEmployeeId = Guid.NewGuid(),
                ScheduledAt = Now.AddDays(3),
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, creator.Created.Count);
        Assert.Equal(TaskActionType.Complete, Assert.Single(completer.Calls).ActionType);
        Assert.Equal(TaskActionType.Review, Assert.Single(canceller.Calls).ActionType);
        await using var verify = fixture.BuildContext();
        Assert.NotNull((await verify.InterviewTaskEffects.SingleAsync(e => e.CompanyId == companyId)).CompletedAt);
    }
}
