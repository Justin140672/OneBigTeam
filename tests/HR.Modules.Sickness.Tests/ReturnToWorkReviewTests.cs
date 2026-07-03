using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Tests;

public class ReturnToWorkReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid SicknessRecordId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ReviewedBy = Guid.NewGuid();
    private static readonly DateOnly DueDate = new(2026, 7, 9);

    private static ReturnToWorkReview CreateDefault() =>
        ReturnToWorkReview.Create(Id, CompanyId, SicknessRecordId, EmployeeId, DueDate, Now);

    [Fact]
    public void Create_SetsAllFields()
    {
        var review = CreateDefault();

        Assert.Equal(Id, review.Id);
        Assert.Equal(CompanyId, review.CompanyId);
        Assert.Equal(SicknessRecordId, review.SicknessRecordId);
        Assert.Equal(EmployeeId, review.EmployeeId);
        Assert.Equal(DueDate, review.DueDate);
        Assert.Equal(ReturnToWorkReviewStatus.Pending, review.Status);
        Assert.Equal(Now, review.CreatedAt);
        Assert.Equal(Now, review.UpdatedAt);
        Assert.Null(review.ReviewedBy);
        Assert.Null(review.Notes);
        Assert.Null(review.CompletedAt);
    }

    [Fact]
    public void Create_SetsStatusToPending()
    {
        var review = CreateDefault();

        Assert.Equal(ReturnToWorkReviewStatus.Pending, review.Status);
    }

    [Fact]
    public void Complete_SetsStatusCompletedReviewedByNotesAndCompletedAt()
    {
        var review = CreateDefault();
        var completedAt = Now.AddDays(1);

        review.Complete(ReviewedBy, "Employee confirmed fit to return.", completedAt);

        Assert.Equal(ReturnToWorkReviewStatus.Completed, review.Status);
        Assert.Equal(ReviewedBy, review.ReviewedBy);
        Assert.Equal("Employee confirmed fit to return.", review.Notes);
        Assert.Equal(completedAt, review.CompletedAt);
        Assert.Equal(completedAt, review.UpdatedAt);
    }

    [Fact]
    public void Complete_WithNullNotes_SetsNullNotes()
    {
        var review = CreateDefault();

        review.Complete(ReviewedBy, null, Now.AddDays(1));

        Assert.Null(review.Notes);
    }

    [Fact]
    public void Cancel_SetsStatusCancelled()
    {
        var review = CreateDefault();
        var cancelledAt = Now.AddDays(1);

        review.Cancel(cancelledAt);

        Assert.Equal(ReturnToWorkReviewStatus.Cancelled, review.Status);
        Assert.Equal(cancelledAt, review.UpdatedAt);
        Assert.Null(review.CompletedAt);
    }

    [Fact]
    public void MarkOverdue_SetsStatusOverdue()
    {
        var review = CreateDefault();
        var overdueAt = Now.AddDays(7);

        review.MarkOverdue(overdueAt);

        Assert.Equal(ReturnToWorkReviewStatus.Overdue, review.Status);
        Assert.Equal(overdueAt, review.UpdatedAt);
        Assert.Null(review.CompletedAt);
    }
}
