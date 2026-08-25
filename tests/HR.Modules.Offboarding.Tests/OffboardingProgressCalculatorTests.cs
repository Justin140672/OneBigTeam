using HR.Modules.Offboarding.Domain;

namespace HR.Modules.Offboarding.Tests;

// OFF-07: pins OffboardingProgressCalculator as the single source of truth for progress reporting —
// every reader (GetOffboardingOverview, OffboardingReportReader, the Blazor tab) must see identical
// numbers, and CanComplete here must always agree with OffboardingPlan.CanComplete.
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
        Assert.Equal(67, summary.ProgressPercent); // 2/3 = 66.67% rounds to 67
        Assert.False(summary.CanComplete); // pendingMandatory is still outstanding
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

        // Both are "resolved" (terminal) for progress-bar purposes...
        Assert.Equal(2, summary.ResolvedTasks);
        Assert.Equal(100, summary.ProgressPercent);
        // ...but a skipped mandatory task must never allow the plan to complete.
        Assert.False(summary.CanComplete);
        Assert.Equal(OffboardingPlan.CanComplete(tasks), summary.CanComplete);
    }
}
