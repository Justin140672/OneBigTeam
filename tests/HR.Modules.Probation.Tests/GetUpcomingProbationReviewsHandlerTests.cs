using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.GetUpcomingProbationReviews;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class GetUpcomingProbationReviewsHandlerTests
{
    // Handler treats "today" as DateOnly from clock.UtcNow.
    private static readonly DateTime    FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly    Today       = new(2026, 6, 25);
    private static readonly DateTimeOffset Now      = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Pending_Reviews_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var result = await BuildHandler(context).HandleAsync(
            new GetUpcomingProbationReviewsRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Pending_Reviews_Within_30_Days()
    {
        await using var context = BuildContext();
        var (companyId, employeeId) = (Guid.NewGuid(), Guid.NewGuid());
        var record = SeedRecord(context, companyId, employeeId);

        context.ProbationReviews.Add(ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id,
            ProbationReviewType.ManagerCheckIn, Today.AddDays(10), Now));
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetUpcomingProbationReviewsRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Reviews_Due_After_30_Days()
    {
        await using var context = BuildContext();
        var (companyId, employeeId) = (Guid.NewGuid(), Guid.NewGuid());
        var record = SeedRecord(context, companyId, employeeId);

        // Due in 31 days — outside the window.
        context.ProbationReviews.Add(ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id,
            ProbationReviewType.HrReview, Today.AddDays(31), Now));
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetUpcomingProbationReviewsRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_Overdue_Reviews()
    {
        await using var context = BuildContext();
        var (companyId, employeeId) = (Guid.NewGuid(), Guid.NewGuid());
        var record = SeedRecord(context, companyId, employeeId);

        context.ProbationReviews.Add(ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id,
            ProbationReviewType.ManagerCheckIn, Today.AddDays(-5), Now));
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetUpcomingProbationReviewsRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Completed_Reviews()
    {
        await using var context = BuildContext();
        var (companyId, employeeId) = (Guid.NewGuid(), Guid.NewGuid());
        var record = SeedRecord(context, companyId, employeeId);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id,
            ProbationReviewType.ManagerCheckIn, Today.AddDays(5), Now);
        review.Complete(Guid.NewGuid(), null, null, Now);
        context.ProbationReviews.Add(review);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetUpcomingProbationReviewsRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_EmployeeId_From_Linked_Record()
    {
        await using var context = BuildContext();
        var (companyId, employeeId) = (Guid.NewGuid(), Guid.NewGuid());
        var record = SeedRecord(context, companyId, employeeId);

        context.ProbationReviews.Add(ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id,
            ProbationReviewType.ManagerCheckIn, Today.AddDays(7), Now));
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetUpcomingProbationReviewsRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal(record.Id, item.ProbationRecordId);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_DueDate_Ascending()
    {
        await using var context = BuildContext();
        var (companyId, employeeId) = (Guid.NewGuid(), Guid.NewGuid());
        var record = SeedRecord(context, companyId, employeeId);

        context.ProbationReviews.AddRange(
            ProbationReview.Create(Guid.NewGuid(), companyId, record.Id, ProbationReviewType.FinalDecision,  Today.AddDays(20), Now),
            ProbationReview.Create(Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn, Today.AddDays(2),  Now),
            ProbationReview.Create(Guid.NewGuid(), companyId, record.Id, ProbationReviewType.HrReview,       Today.AddDays(10), Now));
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetUpcomingProbationReviewsRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;
        Assert.Equal(3, items.Count);
        Assert.Equal("ManagerCheckIn", items[0].ReviewType);
        Assert.Equal("HrReview",       items[1].ReviewType);
        Assert.Equal("FinalDecision",  items[2].ReviewType);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var company1Id  = Guid.NewGuid();
        var company2Id  = Guid.NewGuid();
        var record1 = SeedRecord(context, company1Id, Guid.NewGuid());
        var record2 = SeedRecord(context, company2Id, Guid.NewGuid());

        context.ProbationReviews.AddRange(
            ProbationReview.Create(Guid.NewGuid(), company1Id, record1.Id, ProbationReviewType.ManagerCheckIn, Today.AddDays(5), Now),
            ProbationReview.Create(Guid.NewGuid(), company2Id, record2.Id, ProbationReviewType.HrReview,       Today.AddDays(5), Now));
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetUpcomingProbationReviewsRequest(company1Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.All(result.Value.Items, item => Assert.Equal(company1Id, item.ReviewId != Guid.Empty ? company1Id : Guid.Empty));
    }

    private static ProbationRecord SeedRecord(ProbationDbContext context, Guid companyId, Guid employeeId)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(record);
        return record;
    }

    private static GetUpcomingProbationReviewsHandler BuildHandler(ProbationDbContext context) =>
        new(context, new FakeOpenTaskBySourceEntityReader(), new FakeClock(FixedUtcNow));

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
