using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

public class InterviewOutcomeTaskReconciliationPostgresTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private async Task<(Guid CompanyId, Guid VacancyId, Guid ApplicationId, Guid InterviewId)> SeedAsync()
    {
        await using var db = fixture.BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"{Guid.NewGuid():N}@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, Now);
        application.SetInterviewOutcome(InterviewOutcome.Pending, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();
        return (companyId, vacancy.Id, application.Id, interview.Id);
    }

    private static InterviewOutcomeTaskReconciliationService Service(
        Persistence.RecruitmentDbContext db, FakeTaskCompleter completer, FakeTaskCanceller canceller) =>
        new(db, new FakeTaskResolution(completer, canceller), OutcomeWiring.Delivery(db, new FakeAuditPublisher()), new FakeClock(FixedUtcNow),
            NullLogger<InterviewOutcomeTaskReconciliationService>.Instance);

    [Fact]
    public async Task Outcome_And_Reconciliation_Record_Commit_Together_And_Concurrent_Workers_Run_Side_Effects_Once()
    {
        var (companyId, vacancyId, applicationId, interviewId) = await SeedAsync();
        var failingCompleter = new FakeTaskCompleter { Fail = true };

        await using (var db = fixture.BuildContext())
        {
            var handler = new RecordInterviewOutcomeHandler(
                OutcomeWiring.Recorder(db, new FakeAuditPublisher()),
                Service(db, failingCompleter, new FakeTaskCanceller()));

            var result = await handler.HandleAsync(
                new RecordInterviewOutcomeRequest
                {
                    CompanyId = companyId,
                    VacancyId = vacancyId,
                    ApplicationId = applicationId,
                    InterviewId = interviewId,
                    Outcome = InterviewOutcome.Passed,
                },
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using (var verify = fixture.BuildContext())
        {
            Assert.Equal(InterviewOutcome.Passed, (await verify.Interviews.SingleAsync(i => i.Id == interviewId)).Outcome);
            Assert.Null((await verify.InterviewOutcomeTaskReconciliations.SingleAsync(r => r.InterviewId == interviewId)).CompletedAt);
        }

        var completer = new FakeTaskCompleter();
        var canceller = new FakeTaskCanceller();
        await using var dbA = fixture.BuildContext();
        await using var dbB = fixture.BuildContext();
        var recordA = await dbA.InterviewOutcomeTaskReconciliations.SingleAsync(r => r.InterviewId == interviewId);
        var recordB = await dbB.InterviewOutcomeTaskReconciliations.SingleAsync(r => r.InterviewId == interviewId);

        var results = await Task.WhenAll(
            Service(dbA, completer, canceller).RunAsync(recordA, CancellationToken.None),
            Service(dbB, completer, canceller).RunAsync(recordB, CancellationToken.None));

        Assert.Single(results, r => r);
        Assert.Single(completer.Calls);
        Assert.Single(canceller.Calls);
        await using var final = fixture.BuildContext();
        Assert.NotNull((await final.InterviewOutcomeTaskReconciliations.SingleAsync(r => r.InterviewId == interviewId)).CompletedAt);
    }
}
