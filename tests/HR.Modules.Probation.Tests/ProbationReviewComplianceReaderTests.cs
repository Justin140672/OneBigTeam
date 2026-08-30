using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class ProbationReviewComplianceReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 30);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ProbationRecord SeedRecord(ProbationDbContext db, Guid companyId, Guid employeeId)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 1), null, Today, Now);
        db.ProbationRecords.Add(record);
        return record;
    }

    private static async Task<IReadOnlyList<HR.Infrastructure.Abstractions.ProbationReviewComplianceItem>> Read(
        ProbationDbContext db, Guid companyId) =>
        await new ProbationReviewComplianceReader(db)
            .GetPendingProbationReviewsAsync(companyId, CancellationToken.None);

    [Fact]
    public async Task Returns_Only_Pending_Reviews()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = SeedRecord(db, companyId, Guid.NewGuid());

        var pending = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn, Today.AddDays(5), Now);

        var completed = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.HrReview, Today.AddDays(5), Now);
        completed.Complete(Guid.NewGuid(), null, null, Now);

        var cancelled = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.FinalDecision, Today.AddDays(5), Now);
        cancelled.Cancel(null, Now);

        db.ProbationReviews.AddRange(pending, completed, cancelled);
        await db.SaveChangesAsync();

        var item = Assert.Single(await Read(db, companyId));
        Assert.Equal(pending.Id, item.ProbationReviewId);
    }

    [Fact]
    public async Task Returns_One_Row_Per_Pending_Review()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = SeedRecord(db, companyId, Guid.NewGuid());
        db.ProbationReviews.AddRange(
            ProbationReview.Create(Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn, Today.AddDays(5), Now),
            ProbationReview.Create(Guid.NewGuid(), companyId, record.Id, ProbationReviewType.HrReview, Today.AddDays(20), Now));
        await db.SaveChangesAsync();

        Assert.Equal(2, (await Read(db, companyId)).Count);
    }

    [Fact]
    public async Task Is_Scoped_By_CompanyId()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var recordA = SeedRecord(db, companyA, Guid.NewGuid());
        var recordB = SeedRecord(db, companyB, Guid.NewGuid());
        db.ProbationReviews.AddRange(
            ProbationReview.Create(Guid.NewGuid(), companyA, recordA.Id, ProbationReviewType.ManagerCheckIn, Today.AddDays(5), Now),
            ProbationReview.Create(Guid.NewGuid(), companyB, recordB.Id, ProbationReviewType.ManagerCheckIn, Today.AddDays(5), Now));
        await db.SaveChangesAsync();

        Assert.Single(await Read(db, companyA));
    }

    [Fact]
    public async Task Surfaces_EmployeeId_DueDate_And_ReviewType()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var record = SeedRecord(db, companyId, employeeId);
        var dueDate = Today.AddDays(12);
        db.ProbationReviews.Add(ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.HrReview, dueDate, Now));
        await db.SaveChangesAsync();

        var item = Assert.Single(await Read(db, companyId));
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal(dueDate, item.DueDate);
        Assert.Equal("HrReview", item.ReviewType);
    }

    [Fact]
    public async Task Returns_Empty_When_No_Records_Exist()
    {
        await using var db = BuildContext();
        Assert.Empty(await Read(db, Guid.NewGuid()));
    }
}
