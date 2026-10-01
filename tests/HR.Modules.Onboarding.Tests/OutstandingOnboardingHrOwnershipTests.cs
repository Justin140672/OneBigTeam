using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Services;
using HR.Modules.Onboarding.Tests.Infrastructure;

namespace HR.Modules.Onboarding.Tests;

public class OutstandingOnboardingHrOwnershipTests
{
    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static OutstandingOnboardingTasksWorkloadActionProvider HrProvider(
        Guid callerId, Guid onboardingTaskId, Guid linkedTaskId, Guid? assignee, string owner = "Manager") =>
        new(
            new FakeOnboardingReportReader(
            [
                new OnboardingReportItem(Guid.NewGuid(), Guid.NewGuid(), "InProgress", new DateOnly(2026, 7, 1), 1, 0,
                [
                    new OnboardingReportTaskItem("Set up workstation", null, owner, false, onboardingTaskId),
                ]),
            ]),
            new FakeDirectReportsReader(),
            new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(
                new Dictionary<Guid, Guid> { [onboardingTaskId] = linkedTaskId },
                new Dictionary<Guid, Guid?> { [linkedTaskId] = assignee }),
            new FakeCurrentUser(callerId));

    [Fact]
    public async Task HrScope_TaskAssignedToCaller_CanAct()
    {
        var callerId = Guid.NewGuid();
        var provider = HrProvider(callerId, Guid.NewGuid(), Guid.NewGuid(), assignee: callerId);

        var action = Assert.Single(await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
        Assert.NotNull(action.TaskId);
    }

    [Fact]
    public async Task HrScope_UnassignedTask_IsInTheHrQueue_AndCanAct()
    {
        var callerId = Guid.NewGuid();
        var provider = HrProvider(callerId, Guid.NewGuid(), Guid.NewGuid(), assignee: null, owner: "HR");

        var action = Assert.Single(await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
    }

    [Fact]
    public async Task HrScope_TaskAssignedToManagerOrEmployee_IsVisibilityOnly_AndNamesTheResponsibleParty()
    {
        var callerId = Guid.NewGuid();
        var provider = HrProvider(callerId, Guid.NewGuid(), Guid.NewGuid(), assignee: Guid.NewGuid(), owner: "Manager");

        var action = Assert.Single(await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
        Assert.False(action.IsOwnerActionable);
        Assert.Equal("Assigned to Manager", action.OwnerLabel);
        Assert.False(string.IsNullOrWhiteSpace(action.VisibilityReason));
    }

    [Fact]
    public async Task HrScope_TaskBecomesActionableAfterEscalationToTheHrQueue()
    {
        var callerId = Guid.NewGuid();
        var onboardingTaskId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var before = Assert.Single(await HrProvider(callerId, onboardingTaskId, linkedTaskId, Guid.NewGuid())
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));
        var after = Assert.Single(await HrProvider(callerId, onboardingTaskId, linkedTaskId, assignee: null, owner: "HR")
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.VisibilityOnly, before.Actionability);
        Assert.Equal(WorkloadActionability.CanAct, after.Actionability);
    }
}
