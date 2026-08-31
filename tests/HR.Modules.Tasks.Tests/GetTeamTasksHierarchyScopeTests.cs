using System.Security.Claims;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.GetTeamTasks;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

/// <summary>
/// DSH-02: "my team" for the team-tasks dashboard widget and the Manager Tasks Overdue workload
/// provider is the manager's <em>entire</em> reporting sub-tree — direct <em>and</em> indirect
/// reports — resolved via <c>IDirectReportsReader.GetAllDescendantIdsAsync</c>. A peer / unrelated
/// manager's reports stay out of scope. See
/// specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
public class GetTeamTasksHierarchyScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static TasksDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static TaskItem OpenTask(Guid companyId, Guid assignee, string title, DateOnly? due = null) =>
        TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), title, null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            due, assignee, null, Now);

    [Fact]
    public async Task Handler_Includes_Indirect_Report_Excludes_Peers_Keeps_Direct_Report()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var seniorManager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var directReport = Guid.NewGuid();   // direct report of seniorManager
        var indirectReport = Guid.NewGuid(); // reports to lineManager -> indirect for seniorManager
        var peerManager = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (seniorManager, lineManager),
            (seniorManager, directReport),
            (lineManager, indirectReport),
            (peerManager, peerReport));

        context.TaskItems.AddRange(
            OpenTask(companyId, directReport, "Direct report task"),
            OpenTask(companyId, indirectReport, "Indirect report task"),
            OpenTask(companyId, peerReport, "Peer team task"),
            OpenTask(companyId, peerManager, "Peer manager task"));
        await context.SaveChangesAsync();

        var handler = new GetTeamTasksHandler(context, reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(
            new GetTeamTasksRequest { CompanyId = companyId, ManagerId = seniorManager },
            CancellationToken.None);

        var titles = result.Items.Select(i => i.Title).ToHashSet();
        Assert.Contains("Direct report task", titles);
        Assert.Contains("Indirect report task", titles);
        Assert.DoesNotContain("Peer team task", titles);
        Assert.DoesNotContain("Peer manager task", titles);
    }

    [Fact]
    public async Task ManagerTasksOverdueProvider_Includes_Indirect_Report()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var overdue = new DateOnly(2026, 6, 1);

        var manager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (manager, lineManager),
            (lineManager, indirectReport),
            (Guid.NewGuid(), peerReport));

        context.TaskItems.AddRange(
            OpenTask(companyId, indirectReport, "Indirect overdue", overdue),
            OpenTask(companyId, peerReport, "Peer overdue", overdue));
        await context.SaveChangesAsync();

        var provider = new ManagerTasksOverdueWorkloadActionProvider(
            context, reader, new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeCurrentUser(manager));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(manager), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(indirectReport, action.EmployeeId);
    }

    [Fact]
    public async Task ManagerTasksOverdueProvider_HrCaller_Still_Sees_Company_Wide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var overdue = new DateOnly(2026, 6, 1);
        var caller = Guid.NewGuid();

        context.TaskItems.AddRange(
            OpenTask(companyId, Guid.NewGuid(), "A", overdue),
            OpenTask(companyId, Guid.NewGuid(), "B", overdue));
        await context.SaveChangesAsync();

        var provider = new ManagerTasksOverdueWorkloadActionProvider(
            context, FakeDirectReportsReader.WithHierarchy(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeCurrentUser(caller));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(caller), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ManagerTasksOverdueProvider_NonManager_With_Empty_Subtree_Is_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var caller = Guid.NewGuid();

        context.TaskItems.Add(OpenTask(companyId, Guid.NewGuid(), "A", new DateOnly(2026, 6, 1)));
        await context.SaveChangesAsync();

        var provider = new ManagerTasksOverdueWorkloadActionProvider(
            context, FakeDirectReportsReader.WithHierarchy(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeCurrentUser(caller));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(caller), CancellationToken.None);

        Assert.Empty(result);
    }
}
