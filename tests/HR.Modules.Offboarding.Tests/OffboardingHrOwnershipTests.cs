using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingHrOwnershipTests
{
    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static async Task<WorkloadAction> ActionAsync(Guid? assignee, Guid callerId, bool withOpenTask = true)
    {
        var offboardingTaskId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader(
        [
            new OffboardingReportItem(
                Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(5), "InProgress", 1, 0,
                ["Return laptop"], [], DocumentsReturned: false, OutstandingTaskIds: [offboardingTaskId]),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(
                withOpenTask ? new Dictionary<Guid, Guid> { [offboardingTaskId] = linkedTaskId } : null,
                new Dictionary<Guid, Guid?> { [linkedTaskId] = assignee }),
            new FakeCurrentUser(callerId));

        return Assert.Single(await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));
    }

    [Fact]
    public async Task UnassignedTask_IsInTheHrQueue_AndCanAct()
    {
        var action = await ActionAsync(assignee: null, callerId: Guid.NewGuid());

        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
        Assert.NotNull(action.TaskId);
    }

    [Fact]
    public async Task TaskAssignedToAnotherUser_IsVisibilityOnly()
    {
        var action = await ActionAsync(assignee: Guid.NewGuid(), callerId: Guid.NewGuid());

        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
        Assert.False(action.IsOwnerActionable);
        Assert.False(string.IsNullOrWhiteSpace(action.OwnerLabel));
    }

    [Fact]
    public async Task TaskAssignedToCaller_CanAct()
    {
        var caller = Guid.NewGuid();

        var action = await ActionAsync(assignee: caller, callerId: caller);

        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
    }

    [Fact]
    public async Task OutstandingItemWithoutAnOpenTask_HasNoDestination_SoTheComposerTreatsItAsUnavailable()
    {
        var action = await ActionAsync(assignee: null, callerId: Guid.NewGuid(), withOpenTask: false);

        Assert.Null(action.TaskId);
        Assert.True(string.IsNullOrWhiteSpace(action.DeepLinkUrl));
    }
}
