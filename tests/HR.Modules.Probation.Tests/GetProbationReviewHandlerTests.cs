using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.GetProbationReview;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

/// <summary>
/// PROB-02: single-resource read authorization for GetProbationReview — the review/record are
/// fetched first, and CanViewEmployeeAsync is checked against the resolved record.EmployeeId.
/// Unauthorized access must resolve to the same NotFound error as a genuinely nonexistent review
/// id (never a distinct Forbidden), mirroring
/// HR.Modules.Sickness.Tests.GetReturnToWorkReviewHandlerTests.
/// </summary>
public class GetProbationReviewHandlerTests
{
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_HrAdministrator_Can_View_Any_Company_Review()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var hrAdmin = Guid.NewGuid();
        var record = SeedRecord(context, companyId, Guid.NewGuid());
        var review = SeedReview(context, companyId, record.Id);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeRoleAuthorizationService(HrAdministratorRoleId));
        var result = await handler.HandleAsync(
            new GetProbationReviewRequest { CompanyId = companyId, ReviewId = review.Id }, hrAdmin, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(review.Id, result.Value!.Id);
    }

    [Fact]
    public async Task HandleAsync_Direct_Manager_Can_View()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var record = SeedRecord(context, companyId, employeeId);
        var review = SeedReview(context, companyId, record.Id);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context, new FakeRoleAuthorizationService(), new FakeDirectReportsReader(employeeId));
        var result = await handler.HandleAsync(
            new GetProbationReviewRequest { CompanyId = companyId, ReviewId = review.Id }, manager, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(employeeId, result.Value!.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Indirect_GrandParent_Manager_Can_View()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var seniorManager = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var record = SeedRecord(context, companyId, employeeId);
        var review = SeedReview(context, companyId, record.Id);
        await context.SaveChangesAsync();

        // GetAllDescendantIdsAsync is transitive — the senior manager's full descendant set
        // includes the indirect (skip-level) report, resolved here via the fake directly
        // returning the employee.
        var handler = BuildHandler(
            context, new FakeRoleAuthorizationService(), new FakeDirectReportsReader(employeeId));
        var result = await handler.HandleAsync(
            new GetProbationReviewRequest { CompanyId = companyId, ReviewId = review.Id }, seniorManager, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Unrelated_Manager_Gets_NotFound_Not_Forbidden()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var unrelatedManager = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var someoneElsesReport = Guid.NewGuid();
        var record = SeedRecord(context, companyId, employeeId);
        var review = SeedReview(context, companyId, record.Id);
        await context.SaveChangesAsync();

        // The unrelated manager's hierarchy contains someone else entirely, not this review's
        // employee.
        var handler = BuildHandler(
            context, new FakeRoleAuthorizationService(), new FakeDirectReportsReader(someoneElsesReport));
        var result = await handler.HandleAsync(
            new GetProbationReviewRequest { CompanyId = companyId, ReviewId = review.Id }, unrelatedManager, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal("Probation review not found.", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Review_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var caller = Guid.NewGuid();

        var handler = BuildHandler(context, new FakeRoleAuthorizationService());
        var result = await handler.HandleAsync(
            new GetProbationReviewRequest { CompanyId = companyId, ReviewId = Guid.NewGuid() }, caller, CancellationToken.None);

        // Same code/message as the "unrelated manager" denial above — a caller must not be able
        // to distinguish "unrelated review" from "no such review" by the response shape.
        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal("Probation review not found.", result.Error.Message);
    }

    private static ProbationRecord SeedRecord(ProbationDbContext context, Guid companyId, Guid employeeId)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);
        return record;
    }

    private static ProbationReview SeedReview(ProbationDbContext context, Guid companyId, Guid recordId)
    {
        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, recordId, ProbationReviewType.ManagerCheckIn, new DateOnly(2026, 7, 1), Now);
        context.ProbationReviews.Add(review);
        return review;
    }

    private static GetProbationReviewHandler BuildHandler(
        ProbationDbContext context,
        FakeRoleAuthorizationService authorizationService,
        FakeDirectReportsReader? directReportsReader = null) =>
        new(context, new ProbationResourceAuthorizer(authorizationService, directReportsReader ?? new FakeDirectReportsReader()));

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
