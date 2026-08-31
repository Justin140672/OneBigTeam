using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Features.GetTeamOnboarding;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Services;
using HR.Modules.Onboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Tests;

/// <summary>
/// DSH-02: the team-onboarding widget and the Outstanding Onboarding Tasks workload provider scope
/// to the manager's entire reporting sub-tree (direct and indirect reports) via
/// <c>GetAllDescendantIdsAsync</c>. A peer / unrelated manager's reports are excluded. See
/// specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
public class GetTeamOnboardingHierarchyScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    private static OnboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static void SeedActivePlan(OnboardingDbContext db, Guid companyId, Guid employeeId)
    {
        var plan = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, Now);
        plan.Start(Now);
        db.OnboardingPlans.Add(plan);
    }

    [Fact]
    public async Task Handler_Includes_Indirect_Report_Excludes_Peers_Keeps_Direct_Report()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var seniorManager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var directReport = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (seniorManager, lineManager),
            (seniorManager, directReport),
            (lineManager, indirectReport),
            (Guid.NewGuid(), peerReport));

        SeedActivePlan(db, companyId, directReport);
        SeedActivePlan(db, companyId, indirectReport);
        SeedActivePlan(db, companyId, peerReport);
        await db.SaveChangesAsync();

        var handler = new GetTeamOnboardingHandler(db, reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = companyId, ManagerId = seniorManager },
            CancellationToken.None);

        var ids = result.Items.Select(i => i.EmployeeId).ToHashSet();
        Assert.Contains(directReport, ids);
        Assert.Contains(indirectReport, ids);
        Assert.DoesNotContain(peerReport, ids);
    }

    [Fact]
    public async Task OutstandingOnboardingTasksProvider_Includes_Indirect_Report_Excludes_Peer()
    {
        var manager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (manager, lineManager),
            (lineManager, indirectReport),
            (Guid.NewGuid(), peerReport));

        var reportReader = new FakeOnboardingReportReader(
        [
            new OnboardingReportItem(indirectReport, Guid.NewGuid(), "InProgress", new DateOnly(2026, 7, 1), 1, 0,
                [new OnboardingReportTaskItem("Indirect task", null, "Manager", false)]),
            new OnboardingReportItem(peerReport, Guid.NewGuid(), "InProgress", new DateOnly(2026, 7, 1), 1, 0,
                [new OnboardingReportTaskItem("Peer task", null, "Manager", false)]),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reportReader, reader, new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-onboarding"), new FakeCurrentUser(manager));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(manager), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(indirectReport, action.EmployeeId);
    }

    [Fact]
    public async Task OutstandingOnboardingTasksProvider_HrCaller_Still_Sees_Company_Wide()
    {
        var caller = Guid.NewGuid();
        var reportReader = new FakeOnboardingReportReader(
        [
            new OnboardingReportItem(Guid.NewGuid(), Guid.NewGuid(), "InProgress", new DateOnly(2026, 7, 1), 1, 0,
                [new OnboardingReportTaskItem("A", null, "HR", false)]),
            new OnboardingReportItem(Guid.NewGuid(), Guid.NewGuid(), "InProgress", new DateOnly(2026, 7, 1), 1, 0,
                [new OnboardingReportTaskItem("B", null, "HR", false)]),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reportReader, FakeDirectReportsReader.WithHierarchy(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeCurrentUser(caller));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(caller), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task OutstandingOnboardingTasksProvider_NonManager_With_Empty_Subtree_Is_Empty()
    {
        var caller = Guid.NewGuid();
        var reportReader = new FakeOnboardingReportReader(
        [
            new OnboardingReportItem(Guid.NewGuid(), Guid.NewGuid(), "InProgress", new DateOnly(2026, 7, 1), 1, 0,
                [new OnboardingReportTaskItem("A", null, "Manager", false)]),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reportReader, FakeDirectReportsReader.WithHierarchy(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeCurrentUser(caller));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(caller), CancellationToken.None);

        Assert.Empty(result);
    }
}
