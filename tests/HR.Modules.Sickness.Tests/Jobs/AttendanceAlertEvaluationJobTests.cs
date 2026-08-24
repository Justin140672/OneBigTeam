using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests.Jobs;

/// <summary>
/// SICK-04: AttendanceAlertEvaluationJob persists candidates from the deterministic
/// AttendanceAlertEvaluationService and must be safe to re-run (Hangfire retry / repeated daily
/// execution) without ever creating duplicate AttendanceAlert rows for the same employee+rule+
/// evidence window. Mirrors FitNoteRequestJobTests' in-memory-DB pattern.
/// </summary>
public class AttendanceAlertEvaluationJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static AttendanceAlertEvaluationJob BuildJob(SicknessDbContext db) =>
        new(db,
            new FakeCompanySicknessSettingsReader(),
            new AttendanceAlertEvaluationService(),
            new FakeClock(FixedUtcNow));

    private static async Task<Guid> SeedCategory(SicknessDbContext db, Guid companyId)
    {
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, Now);
        db.SicknessCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static SicknessRecord CreateClosedRecord(
        Guid companyId, Guid employeeId, Guid categoryId, DateOnly startDate, DateOnly endDate) =>
        SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId,
            startDate, SicknessDayPart.FullDay,
            endDate, SicknessDayPart.FullDay,
            totalDays: 1m, notes: null,
            SicknessEvidenceStatus.NotRequired, Now);

    [Fact]
    public async Task ExecuteAsync_CreatesAlert_WhenFrequentAbsenceRuleFires()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        db.SicknessRecords.AddRange(
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 6)));
        await db.SaveChangesAsync();

        var job = BuildJob(db);
        await job.ExecuteAsync();

        var alerts = await db.AttendanceAlerts.ToListAsync();
        Assert.Single(alerts, a => a.Rule == AttendanceAlertRule.FrequentAbsences);
        Assert.Equal(companyId, alerts[0].CompanyId);
        Assert.Equal(employeeId, alerts[0].EmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_IsIdempotent_OnRepeatedExecution()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        db.SicknessRecords.AddRange(
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 6)));
        await db.SaveChangesAsync();

        var job = BuildJob(db);

        await job.ExecuteAsync();
        await job.ExecuteAsync();
        await job.ExecuteAsync();

        var alerts = await db.AttendanceAlerts.ToListAsync();
        Assert.Single(alerts, a => a.Rule == AttendanceAlertRule.FrequentAbsences);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCreateDuplicate_WhenAlertAlreadyExistsForSameEvidenceWindow()
    {
        // Simulates a job run on a previous day that already raised the alert for this exact
        // employee+rule+evidence-window key; a fresh run against the same underlying data must
        // not add a second row.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        db.SicknessRecords.AddRange(
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6)),
            CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 6)));

        db.AttendanceAlerts.Add(AttendanceAlert.Create(
            Guid.NewGuid(), companyId, employeeId, AttendanceAlertRule.FrequentAbsences,
            new DateOnly(2026, 1, 5), Today, 4, "pre-existing", Now));
        await db.SaveChangesAsync();

        var job = BuildJob(db);
        await job.ExecuteAsync();

        var alerts = await db.AttendanceAlerts.Where(a => a.Rule == AttendanceAlertRule.FrequentAbsences).ToListAsync();
        Assert.Single(alerts);
        Assert.Equal("pre-existing", alerts[0].Description);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCreateAlert_WhenNoRuleFires()
    {
        // Company sickness settings default ReturnToWorkRequiredAfterDays to 1, so a bare closed
        // record with no review would itself trip MissingReturnToWorkReview — a completed review
        // is attached here so this scenario genuinely exercises "no rule fires" rather than
        // accidentally proving the missing-review catch-all instead.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateClosedRecord(companyId, employeeId, categoryId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2));
        db.SicknessRecords.Add(record);

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, record.Id, employeeId, new DateOnly(2026, 6, 3), Now);
        review.Complete(Guid.NewGuid(), FitToReturnOutcome.Fit, adjustmentsRequired: false, adjustmentDetails: null, notes: null, Now);
        db.ReturnToWorkReviews.Add(review);

        await db.SaveChangesAsync();

        var job = BuildJob(db);
        await job.ExecuteAsync();

        Assert.Empty(await db.AttendanceAlerts.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ScopesEvaluation_PerCompany()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryA = await SeedCategory(db, companyA);
        var categoryB = await SeedCategory(db, companyB);

        var companyBRecord = CreateClosedRecord(companyB, employeeId, categoryB, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2));

        db.SicknessRecords.AddRange(
            CreateClosedRecord(companyA, employeeId, categoryA, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6)),
            CreateClosedRecord(companyA, employeeId, categoryA, new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 6)),
            CreateClosedRecord(companyA, employeeId, categoryA, new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6)),
            CreateClosedRecord(companyA, employeeId, categoryA, new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 6)),
            companyBRecord);

        // Company B's lone record would itself trip MissingReturnToWorkReview (default
        // ReturnToWorkRequiredAfterDays = 1) — attach a completed review so the only alert this
        // test proves is FrequentAbsences, scoped correctly to company A.
        var companyBReview = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyB, companyBRecord.Id, employeeId, new DateOnly(2026, 6, 3), Now);
        companyBReview.Complete(Guid.NewGuid(), FitToReturnOutcome.Fit, adjustmentsRequired: false, adjustmentDetails: null, notes: null, Now);
        db.ReturnToWorkReviews.Add(companyBReview);

        await db.SaveChangesAsync();

        var job = BuildJob(db);
        await job.ExecuteAsync();

        // Company A's closed records (each without a review of their own) also individually trip
        // MissingReturnToWorkReview in addition to FrequentAbsences — the assertion that matters
        // for this test is company scoping, so every alert produced must belong to company A and
        // none to company B (whose only record has a completed review attached above).
        var alerts = await db.AttendanceAlerts.ToListAsync();
        Assert.NotEmpty(alerts);
        Assert.All(alerts, a => Assert.Equal(companyA, a.CompanyId));
        Assert.Single(alerts, a => a.Rule == AttendanceAlertRule.FrequentAbsences);
    }
}
