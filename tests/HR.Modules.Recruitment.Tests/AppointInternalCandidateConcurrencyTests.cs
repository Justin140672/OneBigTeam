using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.AppointInternalCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 7: real-PostgreSQL coverage (via <see cref="RecruitmentDatabaseFixture"/>)
/// of two concurrent appoint requests on the same internal application. The Pending save and the
/// completion are both guarded by the application's optimistic-concurrency version, and the Employees
/// side is idempotent on the application's source reference (modelled by a single shared
/// <see cref="FakeEmployeeInternalAppointmentService"/>), so exactly one request completes the
/// application, one Hired stage-history entry is written and one employee change is recorded.
///
/// Deterministic by construction: assertions are on the pair of outcomes and the final database
/// state — never on which request won, nor on which guard stopped the loser.
/// </summary>
public class AppointInternalCandidateConcurrencyTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private const int Rounds = 5;

    private static readonly DateTime FixedUtcNow = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed record Participant(
        RecruitmentDbContext Db,
        FakeIntegrationEventPublisher Events,
        AppointInternalCandidateHandler Handler);

    private sealed record Round(
        Guid CompanyId,
        Guid VacancyId,
        Guid ApplicationId,
        Guid EmployeeId,
        Guid HiredStageId,
        FakeEmployeeApplicantReader ApplicantReader,
        FakePositionProfileReader ProfileReader,
        FakeEmployeeInternalAppointmentService Service);

    private Participant BuildParticipant(Round r)
    {
        var db = fixture.BuildContext();
        var events = new FakeIntegrationEventPublisher();
        var audit = new FakeAuditPublisher();
        var clock = new FakeClock(FixedUtcNow);
        var completer = new InternalAppointmentCompleter(db, clock, events, audit, new RecruitmentStageChangeRecorder(db, events, audit));
        var handler = new AppointInternalCandidateHandler(
            db, r.ApplicantReader, r.ProfileReader, r.Service, completer, clock,
            NullLogger<AppointInternalCandidateHandler>.Instance);
        return new Participant(db, events, handler);
    }

    private async Task<Round> SeedRoundAsync()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        await using var db = fixture.BuildContext();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now.AddDays(-30));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Engineering Manager", null, Guid.NewGuid(), Now.AddDays(-30));
        db.Vacancies.Add(vacancy);
        var (_, application) = InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.Offer.Id, employeeId, Now.AddDays(-10));
        application.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, null, new DateOnly(2026, 9, 20), null, Now.AddDays(-6));
        application.RespondToOffer(OfferResponseStatus.Accepted, Now.AddDays(-2));
        await db.SaveChangesAsync();

        var service = new FakeEmployeeInternalAppointmentService
        {
            BeforeAppoint = () => Task.Delay(25),
        };

        return new Round(
            companyId,
            vacancy.Id,
            application.Id,
            employeeId,
            stages.Hired.Id,
            new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)),
            new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
            {
                [positionProfileId] = new(positionProfileId, "Engineering Manager", Guid.NewGuid(), true, Guid.NewGuid(), "London"),
            }),
            service);
    }

    private static AppointInternalCandidateRequest Request(Round r) => new()
    {
        CompanyId     = r.CompanyId,
        VacancyId     = r.VacancyId,
        ApplicationId = r.ApplicationId,
        EffectiveDate = new DateOnly(2026, 10, 1),
        ManagerId     = Guid.NewGuid(),
    };

    [Fact]
    public async Task Concurrent_Appoints_On_Same_Application_Complete_Exactly_Once()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var r = await SeedRoundAsync();
            var a = BuildParticipant(r);
            var b = BuildParticipant(r);
            await using (a.Db)
            await using (b.Db)
            {
                var taskA = Task.Run(() => a.Handler.HandleAsync(Request(r), Guid.NewGuid(), CancellationToken.None));
                var taskB = Task.Run(() => b.Handler.HandleAsync(Request(r), Guid.NewGuid(), CancellationToken.None));
                var results = await Task.WhenAll(taskA, taskB);

                var winner = Assert.Single(results, x => x.IsSuccess).Value!;
                var loser = Assert.Single(results, x => x.IsFailure);
                Assert.Contains(loser.Error.Code, new[] { "concurrency", "conflict" });

                await using var verify = fixture.BuildContext();

                var application = await verify.Applications.AsNoTracking().SingleAsync(x => x.Id == r.ApplicationId);
                Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
                Assert.Equal(winner.PromotionId, application.AppointmentPromotionId);
                Assert.Equal(r.HiredStageId, application.CurrentStageId);
                Assert.Equal(r.EmployeeId, application.AppointmentEmployeeId);

                var hiredEntries = await verify.ApplicationStageHistoryEntries.AsNoTracking()
                    .Where(e => e.ApplicationId == r.ApplicationId && e.NewStageId == r.HiredStageId)
                    .ToListAsync();
                Assert.Single(hiredEntries);
                Assert.Single(await verify.ApplicationStageHistoryEntries.AsNoTracking()
                    .Where(e => e.ApplicationId == r.ApplicationId)
                    .ToListAsync());

                Assert.Equal(1, r.Service.RecordedCount);
                Assert.All(r.Service.AppointRequests,
                    req => Assert.Equal($"recruitment:application:{r.ApplicationId}", req.SourceReference));

                var appointedEvents = a.Events.PublishedEvents.Concat(b.Events.PublishedEvents)
                    .OfType<InternalCandidateAppointedIntegrationEvent>()
                    .ToList();
                var appointed = Assert.Single(appointedEvents);
                Assert.Equal(winner.PromotionId, appointed.PromotionId);
                Assert.Empty(a.Events.PublishedEvents.Concat(b.Events.PublishedEvents).OfType<CandidateHiredIntegrationEvent>());
            }
        }
    }

    [Fact]
    public async Task Sequential_Retry_After_Completion_Returns_Conflict_And_Records_Nothing_New()
    {
        var r = await SeedRoundAsync();
        var a = BuildParticipant(r);
        await using (a.Db)
        {
            var first = await a.Handler.HandleAsync(Request(r), Guid.NewGuid(), CancellationToken.None);
            Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Message : null);
        }

        var b = BuildParticipant(r);
        await using (b.Db)
        {
            var second = await b.Handler.HandleAsync(Request(r), Guid.NewGuid(), CancellationToken.None);
            Assert.True(second.IsFailure);
            Assert.Equal("conflict", second.Error.Code);
            Assert.Empty(b.Events.PublishedEvents);
        }

        await using var verify = fixture.BuildContext();
        Assert.Single(await verify.ApplicationStageHistoryEntries.AsNoTracking()
            .Where(e => e.ApplicationId == r.ApplicationId)
            .ToListAsync());
        Assert.Single(r.Service.AppointRequests);
        Assert.Equal(1, r.Service.RecordedCount);
    }
}
