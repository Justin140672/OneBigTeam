using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests.Jobs;

/// <summary>
/// Internal recruitment Ticket 7: <see cref="InternalAppointmentReconciliationJob"/> recovers internal
/// appointments left Pending by an interrupted request. Only applications Pending for longer than
/// <see cref="InternalAppointmentReconciliationJob.StaleAfter"/> are touched.
/// </summary>
public class InternalAppointmentReconciliationJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly EffectiveDate = new(2026, 10, 1);

    private sealed record Context(
        RecruitmentDbContext Db,
        Guid CompanyId,
        Guid VacancyId,
        RecruitmentStageTestData.SeededStages Stages,
        FakeEmployeeInternalAppointmentService Service,
        FakeIntegrationEventPublisher Events,
        FakeAuditPublisher Audit)
    {
        public InternalAppointmentReconciliationJob Job()
        {
            var clock = new FakeClock(FixedUtcNow);
            var completer = new InternalAppointmentCompleter(Db, clock, Events, Audit, new RecruitmentStageChangeRecorder(Db, Events, Audit));
            return new InternalAppointmentReconciliationJob(
                Db, Service, completer, clock, NullLogger<InternalAppointmentReconciliationJob>.Instance);
        }

        public async Task<Application> ReloadAsync(Guid applicationId)
        {
            Db.ChangeTracker.Clear();
            return await Db.Applications.SingleAsync(a => a.Id == applicationId);
        }
    }

    private static async Task<Context> SeedCompanyAsync()
    {
        var db = new RecruitmentDbContext(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now.AddDays(-30));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineering Manager", null, Guid.NewGuid(), Now.AddDays(-30));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return new Context(db, companyId, vacancy.Id, stages, new FakeEmployeeInternalAppointmentService(), new FakeIntegrationEventPublisher(), new FakeAuditPublisher());
    }

    private static async Task<(Application Application, Guid EmployeeId, Guid RequestedBy)> AddPendingAsync(Context c, TimeSpan pendingFor)
    {
        var employeeId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var (_, application) = InternalApplicationTestData.AddInternal(c.Db, c.CompanyId, c.VacancyId, c.Stages.Offer.Id, employeeId, Now.AddDays(-10));
        application.BeginInternalAppointment(employeeId, requestedBy, Now - pendingFor);
        await c.Db.SaveChangesAsync();
        return (application, employeeId, requestedBy);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_Stale_Pending_Appointment_When_Change_Was_Recorded()
    {
        var c = await SeedCompanyAsync();
        var (application, employeeId, requestedBy) = await AddPendingAsync(c, TimeSpan.FromMinutes(11));
        var recorded = c.Service.Seed(c.CompanyId, application.InternalAppointmentSourceReference, employeeId, Guid.NewGuid(), EffectiveDate);

        await c.Job().ExecuteAsync();

        var saved = await c.ReloadAsync(application.Id);
        Assert.Equal(InternalAppointmentStatus.Completed, saved.AppointmentStatus);
        Assert.Equal(recorded.PromotionId, saved.AppointmentPromotionId);
        Assert.Equal(EffectiveDate, saved.AppointmentEffectiveDate);
        Assert.Equal(c.Stages.Hired.Id, saved.CurrentStageId);

        var entry = Assert.Single(await c.Db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Equal(c.Stages.Offer.Id, entry.PreviousStageId);
        Assert.Equal(c.Stages.Hired.Id, entry.NewStageId);
        Assert.Equal(requestedBy, entry.ChangedByUserId);

        var appointed = Assert.Single(c.Events.PublishedEvents.OfType<InternalCandidateAppointedIntegrationEvent>());
        Assert.Equal(recorded.PromotionId, appointed.PromotionId);
        Assert.Empty(c.Events.PublishedEvents.OfType<CandidateHiredIntegrationEvent>());
        Assert.Empty(c.Service.AppointRequests);

        Assert.Equal(requestedBy, Assert.Single(c.Service.ResumeCalls).PerformedBy);
    }

    [Fact]
    public async Task ExecuteAsync_Releases_Stale_Pending_Appointment_When_No_Change_Was_Recorded()
    {
        var c = await SeedCompanyAsync();
        var (application, _, _) = await AddPendingAsync(c, TimeSpan.FromMinutes(11));

        await c.Job().ExecuteAsync();

        var saved = await c.ReloadAsync(application.Id);
        Assert.Null(saved.AppointmentStatus);
        Assert.Null(saved.AppointmentEmployeeId);
        Assert.Null(saved.AppointmentRequestedAt);
        Assert.False(saved.HasInternalAppointmentInProgress);
        Assert.Equal(c.Stages.Offer.Id, saved.CurrentStageId);

        Assert.Empty(await c.Db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Empty(c.Events.PublishedEvents);
        Assert.Empty(c.Service.AppointRequests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    public async Task ExecuteAsync_Leaves_Fresh_Pending_Appointment_Untouched(int minutesPending)
    {
        var c = await SeedCompanyAsync();
        var (application, employeeId, _) = await AddPendingAsync(c, TimeSpan.FromMinutes(minutesPending));
        c.Service.Seed(c.CompanyId, application.InternalAppointmentSourceReference, employeeId, Guid.NewGuid(), EffectiveDate);

        await c.Job().ExecuteAsync();

        var saved = await c.ReloadAsync(application.Id);
        Assert.Equal(InternalAppointmentStatus.Pending, saved.AppointmentStatus);
        Assert.Equal(c.Stages.Offer.Id, saved.CurrentStageId);
        Assert.Empty(c.Service.ResumeCalls);
        Assert.Empty(c.Events.PublishedEvents);
    }

    [Fact]
    public async Task ExecuteAsync_Treats_Exactly_StaleAfter_As_Still_Fresh()
    {
        var c = await SeedCompanyAsync();
        var (application, _, _) = await AddPendingAsync(c, InternalAppointmentReconciliationJob.StaleAfter);

        await c.Job().ExecuteAsync();

        Assert.Equal(InternalAppointmentStatus.Pending, (await c.ReloadAsync(application.Id)).AppointmentStatus);
        Assert.Empty(c.Service.ResumeCalls);
    }

    [Fact]
    public async Task ExecuteAsync_Reconciles_Just_Past_StaleAfter()
    {
        var c = await SeedCompanyAsync();
        var (application, _, _) = await AddPendingAsync(c, InternalAppointmentReconciliationJob.StaleAfter + TimeSpan.FromSeconds(1));

        await c.Job().ExecuteAsync();

        Assert.Null((await c.ReloadAsync(application.Id)).AppointmentStatus);
        Assert.Single(c.Service.ResumeCalls);
    }

    [Fact]
    public void StaleAfter_Is_Ten_Minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), InternalAppointmentReconciliationJob.StaleAfter);
    }

    [Fact]
    public async Task ExecuteAsync_Leaves_Completed_Appointment_Untouched()
    {
        var c = await SeedCompanyAsync();
        var (application, _, _) = await AddPendingAsync(c, TimeSpan.FromHours(2));
        var promotionId = Guid.NewGuid();
        application.CompleteInternalAppointment(c.Stages.Hired.Id, promotionId, EffectiveDate, Now.AddHours(-2));
        await c.Db.SaveChangesAsync();
        var versionBefore = application.Version;

        await c.Job().ExecuteAsync();

        var saved = await c.ReloadAsync(application.Id);
        Assert.Equal(InternalAppointmentStatus.Completed, saved.AppointmentStatus);
        Assert.Equal(promotionId, saved.AppointmentPromotionId);
        Assert.Equal(versionBefore, saved.Version);
        Assert.Empty(c.Service.ResumeCalls);
        Assert.Empty(c.Events.PublishedEvents);
    }

    [Fact]
    public async Task ExecuteAsync_Ignores_Applications_With_No_Appointment()
    {
        var c = await SeedCompanyAsync();
        var (_, application) = InternalApplicationTestData.AddInternal(c.Db, c.CompanyId, c.VacancyId, c.Stages.Offer.Id, Guid.NewGuid(), Now.AddDays(-10));
        await c.Db.SaveChangesAsync();

        await c.Job().ExecuteAsync();

        Assert.Null((await c.ReloadAsync(application.Id)).AppointmentStatus);
        Assert.Empty(c.Service.ResumeCalls);
    }

    [Fact]
    public async Task ExecuteAsync_Leaves_Stale_Pending_When_Change_Recorded_But_No_Active_Hired_Stage()
    {
        var c = await SeedCompanyAsync();
        var (application, employeeId, _) = await AddPendingAsync(c, TimeSpan.FromMinutes(30));
        c.Service.Seed(c.CompanyId, application.InternalAppointmentSourceReference, employeeId, Guid.NewGuid(), EffectiveDate);
        c.Stages.Hired.SetActiveStatus(false, Now);
        await c.Db.SaveChangesAsync();

        await c.Job().ExecuteAsync();

        // Must NOT release it: the employee change is recorded, so releasing would let HR appoint twice.
        var saved = await c.ReloadAsync(application.Id);
        Assert.Equal(InternalAppointmentStatus.Pending, saved.AppointmentStatus);
        Assert.Equal(c.Stages.Offer.Id, saved.CurrentStageId);
        Assert.Empty(c.Events.PublishedEvents);
    }

    [Fact]
    public async Task ExecuteAsync_Handles_Mixed_Batch_Independently()
    {
        var c = await SeedCompanyAsync();
        var (recordedApp, recordedEmployee, _) = await AddPendingAsync(c, TimeSpan.FromMinutes(20));
        var (unrecordedApp, _, _) = await AddPendingAsync(c, TimeSpan.FromMinutes(20));
        var (freshApp, _, _) = await AddPendingAsync(c, TimeSpan.FromMinutes(1));
        c.Service.Seed(c.CompanyId, recordedApp.InternalAppointmentSourceReference, recordedEmployee, Guid.NewGuid(), EffectiveDate);

        await c.Job().ExecuteAsync();

        Assert.Equal(InternalAppointmentStatus.Completed, (await c.ReloadAsync(recordedApp.Id)).AppointmentStatus);
        Assert.Null((await c.ReloadAsync(unrecordedApp.Id)).AppointmentStatus);
        Assert.Equal(InternalAppointmentStatus.Pending, (await c.ReloadAsync(freshApp.Id)).AppointmentStatus);
        Assert.Single(c.Events.PublishedEvents.OfType<InternalCandidateAppointedIntegrationEvent>());
    }

    [Fact]
    public async Task ExecuteAsync_Is_Idempotent_On_Repeat_Runs()
    {
        var c = await SeedCompanyAsync();
        var (application, employeeId, _) = await AddPendingAsync(c, TimeSpan.FromMinutes(11));
        c.Service.Seed(c.CompanyId, application.InternalAppointmentSourceReference, employeeId, Guid.NewGuid(), EffectiveDate);

        await c.Job().ExecuteAsync();
        await c.Job().ExecuteAsync();

        Assert.Equal(InternalAppointmentStatus.Completed, (await c.ReloadAsync(application.Id)).AppointmentStatus);
        Assert.Single(await c.Db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Single(c.Events.PublishedEvents.OfType<InternalCandidateAppointedIntegrationEvent>());
    }

    [Fact]
    public async Task ReconcileAsync_Is_NoOp_For_Unknown_Application()
    {
        var c = await SeedCompanyAsync();

        await c.Job().ReconcileAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(c.Service.ResumeCalls);
    }

    [Fact]
    public async Task ReconcileAsync_Is_NoOp_For_Completed_Application()
    {
        var c = await SeedCompanyAsync();
        var (application, _, _) = await AddPendingAsync(c, TimeSpan.FromHours(1));
        application.CompleteInternalAppointment(c.Stages.Hired.Id, Guid.NewGuid(), EffectiveDate, Now);
        await c.Db.SaveChangesAsync();

        await c.Job().ReconcileAsync(application.Id, CancellationToken.None);

        Assert.Empty(c.Service.ResumeCalls);
        Assert.Empty(c.Events.PublishedEvents);
    }
}
