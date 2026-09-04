using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Sickness.Tests.Jobs;

/// <summary>
/// OBT-REM-10: same batch-isolation fix as <see cref="SicknessEvidenceReminderJobBatchIsolationTests"/>,
/// applied to <see cref="ReturnToWorkReminderJob"/>'s Pending → Overdue transition step. A
/// <c>SaveChangesAsync</c> failure for one review must only detach that review, not the whole batch.
/// </summary>
public class ReturnToWorkReminderJobBatchIsolationTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static DbContextOptions<SicknessDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static ReturnToWorkReminderJob BuildJob(SicknessDbContext db, Guid? managerId) =>
        new(db, new FakeNotificationWriter(), new FakeManagerReader(managerId), new FakeClock(FixedUtcNow),
            NullLogger<ReturnToWorkReminderJob>.Instance);

    private static async Task<ReturnToWorkReview> SeedOverdueDueReviewAsync(
        SicknessDbContext db, Guid companyId, Guid employeeId)
    {
        var categoryId = Guid.NewGuid();
        db.SicknessCategories.Add(SicknessCategory.Create(categoryId, companyId, "Cold", 1, Now));

        var record = SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId,
            new DateOnly(2026, 6, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 6, 5), SicknessDayPart.FullDay,
            totalDays: 5m, notes: null,
            evidenceStatus: SicknessEvidenceStatus.NotRequired, now: Now);
        db.SicknessRecords.Add(record);

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, record.Id, employeeId, Today.AddDays(-1), Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();
        return review;
    }

    private static Func<SicknessDbContext, bool> FailOnceForReview(Guid reviewId)
    {
        var alreadyFailed = false;
        return db =>
        {
            if (alreadyFailed) return false;

            var isTargeted = db.ChangeTracker.Entries<ReturnToWorkReview>()
                .Any(e => e.State == EntityState.Modified && e.Entity.Id == reviewId);

            if (!isTargeted) return false;

            alreadyFailed = true;
            return true;
        };
    }

    [Fact]
    public async Task Save_failure_on_one_batch_item_does_not_block_the_others_transition()
    {
        var options = BuildOptions();
        await using var seedDb = new SicknessDbContext(options);
        var companyId = Guid.NewGuid();
        var reviewA = await SeedOverdueDueReviewAsync(seedDb, companyId, Guid.NewGuid());
        var reviewB = await SeedOverdueDueReviewAsync(seedDb, companyId, Guid.NewGuid());
        var reviewC = await SeedOverdueDueReviewAsync(seedDb, companyId, Guid.NewGuid());

        await using var db = new FailingSaveSicknessDbContext(options, FailOnceForReview(reviewB.Id));
        var job = BuildJob(db, managerId: Guid.NewGuid());

        await job.ExecuteAsync();

        await using var verifyDb = new SicknessDbContext(options);
        var updatedA = await verifyDb.ReturnToWorkReviews.SingleAsync(r => r.Id == reviewA.Id);
        var updatedB = await verifyDb.ReturnToWorkReviews.SingleAsync(r => r.Id == reviewB.Id);
        var updatedC = await verifyDb.ReturnToWorkReviews.SingleAsync(r => r.Id == reviewC.Id);

        Assert.Equal(ReturnToWorkReviewStatus.Overdue, updatedA.Status);
        Assert.Equal(ReturnToWorkReviewStatus.Pending, updatedB.Status);
        Assert.Equal(ReturnToWorkReviewStatus.Overdue, updatedC.Status);
    }
}
