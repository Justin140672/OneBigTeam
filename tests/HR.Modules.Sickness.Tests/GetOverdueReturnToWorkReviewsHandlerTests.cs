using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.GetOverdueReturnToWorkReviews;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class GetOverdueReturnToWorkReviewsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DueDate = new(2026, 6, 20);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Returns_Overdue_Reviews_For_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, recordId, employeeId, DueDate, Now);
        review.MarkOverdue(Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetOverdueReturnToWorkReviewsHandler(db, new FakeOpenTaskBySourceEntityReader());
        var result = await handler.HandleAsync(new GetOverdueReturnToWorkReviewsRequest(companyId), null, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(review.Id, item.ReviewId);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal(recordId, item.SicknessRecordId);
        Assert.Equal(DueDate, item.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Populates_TaskId_When_Open_Task_Exists_For_Review()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), DueDate, Now);
        review.MarkOverdue(Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var taskId = Guid.NewGuid();
        var reader = new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid> { [review.Id] = taskId });

        var handler = new GetOverdueReturnToWorkReviewsHandler(db, reader);
        var result = await handler.HandleAsync(new GetOverdueReturnToWorkReviewsRequest(companyId), null, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(taskId, item.TaskId);
    }

    [Fact]
    public async Task HandleAsync_Leaves_TaskId_Null_When_No_Open_Task_Exists_For_Review()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), DueDate, Now);
        review.MarkOverdue(Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var reader = new FakeOpenTaskBySourceEntityReader();

        var handler = new GetOverdueReturnToWorkReviewsHandler(db, reader);
        var result = await handler.HandleAsync(new GetOverdueReturnToWorkReviewsRequest(companyId), null, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Null(item.TaskId);
    }

    [Fact]
    public async Task HandleAsync_Calls_Reader_With_CompanyId_ReviewIds_And_Review_ActionType()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), DueDate, Now);
        review.MarkOverdue(Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var reader = new FakeOpenTaskBySourceEntityReader();

        var handler = new GetOverdueReturnToWorkReviewsHandler(db, reader);
        await handler.HandleAsync(new GetOverdueReturnToWorkReviewsRequest(companyId), null, CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.Equal(TaskActionType.Review, reader.LastActionType);
        Assert.Contains(review.Id, reader.LastSourceEntityIds!);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Pending_Reviews()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), DueDate, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetOverdueReturnToWorkReviewsHandler(db, new FakeOpenTaskBySourceEntityReader());
        var result = await handler.HandleAsync(new GetOverdueReturnToWorkReviewsRequest(companyId), null, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Completed_Reviews()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), DueDate, Now);
        review.Complete(Guid.NewGuid(), FitToReturnOutcome.Fit, false, null, null, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetOverdueReturnToWorkReviewsHandler(db, new FakeOpenTaskBySourceEntityReader());
        var result = await handler.HandleAsync(new GetOverdueReturnToWorkReviewsRequest(companyId), null, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Reviews_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), Guid.NewGuid(), DueDate, Now);
        review.MarkOverdue(Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetOverdueReturnToWorkReviewsHandler(db, new FakeOpenTaskBySourceEntityReader());
        var result = await handler.HandleAsync(new GetOverdueReturnToWorkReviewsRequest(companyId), null, CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
