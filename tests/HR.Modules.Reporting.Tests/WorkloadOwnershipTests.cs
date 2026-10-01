using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests;

public class WorkloadOwnershipTests
{
    private static readonly Guid Viewer = Guid.NewGuid();

    [Fact]
    public void Task_Assigned_To_The_Viewer_Can_Be_Acted_On()
    {
        var decision = WorkloadOwnership.ForHrViewer(Viewer, Viewer, unassignedBelongsToHr: false, "HR User", "Owned by the employee's manager");

        Assert.Equal(WorkloadActionability.CanAct, decision.Actionability);
        Assert.True(decision.IsOwnerActionable);
        Assert.Null(decision.OwnerLabel);
    }

    [Fact]
    public void Unassigned_Task_Is_An_Hr_Action_Only_When_The_Process_Is_Hr_Owned()
    {
        var hrOwned = WorkloadOwnership.ForHrViewer(null, Viewer, unassignedBelongsToHr: true, null, "Assigned to another user");
        var managerOwned = WorkloadOwnership.ForHrViewer(null, Viewer, unassignedBelongsToHr: false, null, "Owned by the employee's manager");

        Assert.Equal(WorkloadActionability.CanAct, hrOwned.Actionability);
        Assert.Equal(WorkloadActionability.VisibilityOnly, managerOwned.Actionability);
        Assert.Equal("Owned by the employee's manager", managerOwned.OwnerLabel);
    }

    [Fact]
    public void Task_Assigned_To_Someone_Else_Is_Visibility_Only_And_Names_The_Responsible_Person()
    {
        var decision = WorkloadOwnership.ForHrViewer(Guid.NewGuid(), Viewer, unassignedBelongsToHr: true, "Priya Shah", "Assigned to another user");

        Assert.Equal(WorkloadActionability.VisibilityOnly, decision.Actionability);
        Assert.Equal("Assigned to Priya Shah", decision.OwnerLabel);
        Assert.Contains("Priya Shah", decision.VisibilityReason);
        Assert.Contains("monitor", decision.VisibilityReason);
    }

    [Fact]
    public void Unknown_Assignee_Name_Falls_Back_To_The_Role_Label()
    {
        var decision = WorkloadOwnership.ForHrViewer(Guid.NewGuid(), Viewer, unassignedBelongsToHr: true, null, "Assigned to another user");

        Assert.Equal("Assigned to another user", decision.OwnerLabel);
    }

    [Fact]
    public void Escalation_To_The_Hr_Queue_Makes_A_Previously_Monitored_Task_Actionable()
    {
        var before = WorkloadOwnership.ForHrViewer(Guid.NewGuid(), Viewer, unassignedBelongsToHr: true, "A Manager", "Assigned to another user");
        var after = WorkloadOwnership.ForHrViewer(null, Viewer, unassignedBelongsToHr: true, null, "Assigned to another user");

        Assert.Equal(WorkloadActionability.VisibilityOnly, before.Actionability);
        Assert.Equal(WorkloadActionability.CanAct, after.Actionability);
    }

    [Fact]
    public void Workload_Action_Actionability_Defaults_From_Owner_Flag_And_Honours_Override()
    {
        var actionable = Action(isOwnerActionable: true);
        var visibility = Action(isOwnerActionable: false);
        var unavailable = Action(isOwnerActionable: true, @override: WorkloadActionability.Unavailable);

        Assert.Equal(WorkloadActionability.CanAct, actionable.Actionability);
        Assert.Equal(WorkloadActionability.VisibilityOnly, visibility.Actionability);
        Assert.Equal(WorkloadActionability.Unavailable, unavailable.Actionability);
    }

    private static WorkloadAction Action(bool isOwnerActionable, WorkloadActionability? @override = null) =>
        new(Guid.NewGuid(), "Employee", null, "Do it", "Category", null, null, "Pending", "/x",
            IsOwnerActionable: isOwnerActionable, ActionabilityOverride: @override);
}
