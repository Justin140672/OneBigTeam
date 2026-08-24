using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.GetReturnToWorkReview;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class GetReturnToWorkReviewHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DueDate = new(2026, 6, 20);
    private static readonly Guid SicknessManagePermissionId = new("00000000-0000-0000-0001-000000000015");

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SicknessResourceAuthorizer BuildHrAuthorizer() =>
        new(new FakePermissionAuthorizationService(SicknessManagePermissionId), new FakeDirectReportsReader());

    private static SicknessResourceAuthorizer BuildManagerAuthorizer(params Guid[] reportIds) =>
        new(new FakePermissionAuthorizationService(), new FakeDirectReportsReader(reportIds));

    [Fact]
    public async Task HandleAsync_Returns_Full_Review_For_HrAdministrator()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, recordId, employeeId, DueDate, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetReturnToWorkReviewHandler(db, BuildHrAuthorizer());
        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = companyId, ReviewId = review.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(review.Id, result.Value!.Id);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(recordId, result.Value.SicknessRecordId);
        Assert.Equal(DueDate, result.Value.DueDate);
        Assert.Equal("Pending", result.Value.Status);
        Assert.Null(result.Value.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Review_For_Manager_In_Reporting_Hierarchy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, recordId, employeeId, DueDate, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetReturnToWorkReviewHandler(db, BuildManagerAuthorizer(employeeId));
        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = companyId, ReviewId = review.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(employeeId, result.Value!.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Trims_Notes_For_Manager_View()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, recordId, employeeId, DueDate, Now);
        review.Complete(Guid.NewGuid(), "Sensitive medical detail", Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetReturnToWorkReviewHandler(db, BuildManagerAuthorizer(employeeId));
        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = companyId, ReviewId = review.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Notes);
    }

    [Fact]
    public async Task HandleAsync_Includes_Notes_For_HrAdministrator()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, recordId, employeeId, DueDate, Now);
        review.Complete(Guid.NewGuid(), "Sensitive medical detail", Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetReturnToWorkReviewHandler(db, BuildHrAuthorizer());
        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = companyId, ReviewId = review.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Sensitive medical detail", result.Value!.Notes);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Manager_Not_In_Reporting_Hierarchy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, recordId, employeeId, DueDate, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        // Manager's reporting hierarchy does not include this review's employee.
        var handler = new GetReturnToWorkReviewHandler(db, BuildManagerAuthorizer(Guid.NewGuid()));
        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = companyId, ReviewId = review.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Review_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new GetReturnToWorkReviewHandler(db, BuildHrAuthorizer());

        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = Guid.NewGuid(), ReviewId = Guid.NewGuid() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Review_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var review = ReturnToWorkReview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DueDate, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetReturnToWorkReviewHandler(db, BuildHrAuthorizer());
        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = Guid.NewGuid(), ReviewId = review.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
