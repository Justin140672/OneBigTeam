using HR.Modules.Offboarding.Domain;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingProgressCalculatorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    private static OffboardingTask MandatoryTask() =>
        OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Mandatory task", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);

    private static OffboardingTask OptionalTask() =>
        OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Optional task", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow, isMandatory: false);

    [Fact]
    public void Calculate_Returns_All_Zero_And_CanComplete_False_For_Empty_Collection()
    {
        var summary = OffboardingProgressCalculator.Calculate([]);

        Assert.Equal(0, summary.TotalTasks);
        Assert.Equal(0, summary.CompletedTasks);
        Assert.Equal(0, summary.SkippedTasks);
        Assert.Equal(0, summary.ResolvedTasks);
        Assert.Equal(0, summary.ProgressPercent);
        Assert.False(summary.CanComplete);
        Assert.Equal(0, summary.RequiredTotal);
        Assert.Equal(0, summary.RequiredResolved);
        Assert.Equal(0, summary.TotalResolved);
        Assert.Equal(0, summary.TotalCount);
    }


    [Fact]
    public void Calculate_Counts_Waived_Mandatory_Task_As_Resolved_In_Required_And_Total_Counters()
    {
        var waivedMandatory = MandatoryTask();
        waivedMandatory.Waive(FixedNow, "Not required for this departure.", Guid.NewGuid());
        var pendingMandatory = MandatoryTask();

        var summary = OffboardingProgressCalculator.Calculate([waivedMandatory, pendingMandatory]);

        Assert.Equal(2, summary.RequiredTotal);
        Assert.Equal(1, summary.RequiredResolved);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.TotalResolved);
    }

    [Fact]
    public void Calculate_Excludes_Cancelled_Tasks_From_Required_And_Total_Counters()
    {
        var completedMandatory = MandatoryTask();
        completedMandatory.Complete(FixedNow);
        var cancelledMandatory = MandatoryTask();
        cancelledMandatory.CancelBecauseLeavingProcessCancelled(FixedNow, Guid.NewGuid());
        var cancelledOptional = OptionalTask();
        cancelledOptional.CancelBecauseLeavingProcessCancelled(FixedNow, Guid.NewGuid());

        var summary = OffboardingProgressCalculator.Calculate(
            [completedMandatory, cancelledMandatory, cancelledOptional]);

        Assert.Equal(1, summary.RequiredTotal);
        Assert.Equal(1, summary.RequiredResolved);
        Assert.Equal(1, summary.TotalCount);
        Assert.Equal(1, summary.TotalResolved);
    }

    [Fact]
    public void Calculate_Includes_Optional_Tasks_In_TotalCount_But_Not_RequiredTotal()
    {
        var mandatoryPending = MandatoryTask();
        var optionalCompleted = OptionalTask();
        optionalCompleted.Complete(FixedNow);

        var summary = OffboardingProgressCalculator.Calculate([mandatoryPending, optionalCompleted]);

        Assert.Equal(1, summary.RequiredTotal);
        Assert.Equal(0, summary.RequiredResolved);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.TotalResolved);
    }

    [Fact]
    public void Calculate_Reports_All_Complete_When_Every_Mandatory_Task_Completed()
    {
        var taskA = MandatoryTask();
        taskA.Complete(FixedNow);
        var taskB = MandatoryTask();
        taskB.Complete(FixedNow);

        var summary = OffboardingProgressCalculator.Calculate([taskA, taskB]);

        Assert.Equal(2, summary.TotalTasks);
        Assert.Equal(2, summary.CompletedTasks);
        Assert.Equal(0, summary.SkippedTasks);
        Assert.Equal(2, summary.ResolvedTasks);
        Assert.Equal(100, summary.ProgressPercent);
        Assert.True(summary.CanComplete);
        Assert.Equal(OffboardingPlan.CanComplete([taskA, taskB]), summary.CanComplete);
    }

    [Fact]
    public void Calculate_Rounds_ProgressPercent_And_Matches_OffboardingPlan_CanComplete_For_Mixed_Tasks()
    {
        var completed = MandatoryTask();
        completed.Complete(FixedNow);
        var skippedOptional = OptionalTask();
        skippedOptional.Skip(FixedNow, "Not applicable.", Guid.NewGuid());
        var pendingMandatory = MandatoryTask();

        var tasks = new[] { completed, skippedOptional, pendingMandatory };
        var summary = OffboardingProgressCalculator.Calculate(tasks);

        Assert.Equal(3, summary.TotalTasks);
        Assert.Equal(1, summary.CompletedTasks);
        Assert.Equal(1, summary.SkippedTasks);
        Assert.Equal(2, summary.ResolvedTasks);
        Assert.Equal(67, summary.ProgressPercent);
        Assert.False(summary.CanComplete);
        Assert.Equal(OffboardingPlan.CanComplete(tasks), summary.CanComplete);
    }

    [Fact]
    public void Calculate_CanComplete_Is_False_When_Mandatory_Task_Is_Skipped_Rather_Than_Completed()
    {
        var completedMandatory = MandatoryTask();
        completedMandatory.Complete(FixedNow);
        var skippedMandatory = MandatoryTask();
        skippedMandatory.Skip(FixedNow, "Not applicable.", Guid.NewGuid());

        var tasks = new[] { completedMandatory, skippedMandatory };
        var summary = OffboardingProgressCalculator.Calculate(tasks);

        Assert.Equal(2, summary.ResolvedTasks);
        Assert.Equal(100, summary.ProgressPercent);
        Assert.False(summary.CanComplete);
        Assert.Equal(OffboardingPlan.CanComplete(tasks), summary.CanComplete);
    }
}
