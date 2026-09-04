using System.Security.Claims;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

/// <summary>
/// DSH-02: the Probation Reviews Due and Overdue Probation Reviews workload providers scope a
/// manager caller to their entire reporting sub-tree (direct and indirect reports) via
/// <c>GetAllDescendantIdsAsync</c>; HR administrators keep the company-wide view. A peer /
/// unrelated manager's reviews are excluded. See
/// specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
public class ProbationReviewsDueHierarchyScopeTests
{
    private static readonly DateOnly Today = new(2026, 7, 29);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static void SeedReview(ProbationDbContext db, Guid companyId, Guid employeeId, DateOnly dueDate)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 1), null,
            DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime), DateTimeOffset.UtcNow);
        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn, dueDate, DateTimeOffset.UtcNow);
        db.ProbationRecords.Add(record);
        db.ProbationReviews.Add(review);
    }

    private static FakeClock ClockAt(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue));

    [Fact]
    public async Task DueProvider_Manager_Includes_Indirect_Report_Excludes_Peer()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var manager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (manager, lineManager),
            (lineManager, indirectReport),
            (Guid.NewGuid(), peerReport));

        SeedReview(db, companyId, indirectReport, Today.AddDays(5));
        SeedReview(db, companyId, peerReport, Today.AddDays(5));
        await db.SaveChangesAsync();

        var provider = new ProbationReviewsDueWorkloadActionProvider(
            db, reader, new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-probation"), new FakeCurrentUser(manager), new FakeOpenTaskBySourceEntityReader(), ClockAt(Today));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(manager), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(indirectReport, action.EmployeeId);
    }

    [Fact]
    public async Task OverdueProvider_Manager_Includes_Indirect_Report_Excludes_Peer()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var manager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (manager, lineManager),
            (lineManager, indirectReport),
            (Guid.NewGuid(), peerReport));

        SeedReview(db, companyId, indirectReport, Today.AddDays(-3));
        SeedReview(db, companyId, peerReport, Today.AddDays(-3));
        await db.SaveChangesAsync();

        var provider = new OverdueProbationReviewsWorkloadActionProvider(
            db, reader, new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-probation"), new FakeCurrentUser(manager), new FakeOpenTaskBySourceEntityReader(), ClockAt(Today));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(manager), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(indirectReport, action.EmployeeId);
    }

    [Fact]
    public async Task DueProvider_HrCaller_Still_Sees_Company_Wide()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var caller = Guid.NewGuid();

        SeedReview(db, companyId, Guid.NewGuid(), Today.AddDays(5));
        SeedReview(db, companyId, Guid.NewGuid(), Today.AddDays(6));
        await db.SaveChangesAsync();

        var provider = new ProbationReviewsDueWorkloadActionProvider(
            db, FakeDirectReportsReader.WithHierarchy(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeCurrentUser(caller), new FakeOpenTaskBySourceEntityReader(), ClockAt(Today));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(caller), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task DueProvider_NonManager_With_Empty_Subtree_Is_Empty()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var caller = Guid.NewGuid();

        SeedReview(db, companyId, Guid.NewGuid(), Today.AddDays(5));
        await db.SaveChangesAsync();

        var provider = new ProbationReviewsDueWorkloadActionProvider(
            db, FakeDirectReportsReader.WithHierarchy(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-probation"), new FakeCurrentUser(caller), new FakeOpenTaskBySourceEntityReader(), ClockAt(Today));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(caller), CancellationToken.None);

        Assert.Empty(result);
    }
}
