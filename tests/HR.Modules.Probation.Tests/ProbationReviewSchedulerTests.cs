using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Services;

namespace HR.Modules.Probation.Tests;

public class ProbationReviewSchedulerTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    [Fact]
    public void BuildSchedule_Default_Checkpoints_Produce_ManagerCheckIn_HrReview_And_FinalDecision()
    {
        var expectedEndDate = new DateOnly(2026, 4, 1); // 90-day probation

        var schedule = ProbationReviewScheduler.BuildSchedule(StartDate, expectedEndDate, [30, 60, 90]);

        Assert.Equal(3, schedule.Count);
        Assert.Equal((ProbationReviewType.ManagerCheckIn, StartDate.AddDays(30)), schedule[0]);
        Assert.Equal((ProbationReviewType.HrReview, StartDate.AddDays(60)), schedule[1]);
        Assert.Equal((ProbationReviewType.FinalDecision, expectedEndDate), schedule[2]);
    }

    [Fact]
    public void BuildSchedule_Custom_Checkpoints_Produce_Matching_Due_Dates()
    {
        var expectedEndDate = new DateOnly(2026, 6, 1);

        var schedule = ProbationReviewScheduler.BuildSchedule(StartDate, expectedEndDate, [14, 45]);

        Assert.Equal(3, schedule.Count);
        Assert.Equal((ProbationReviewType.ManagerCheckIn, StartDate.AddDays(14)), schedule[0]);
        Assert.Equal((ProbationReviewType.HrReview, StartDate.AddDays(45)), schedule[1]);
        Assert.Equal((ProbationReviewType.FinalDecision, expectedEndDate), schedule[2]);
    }

    [Fact]
    public void BuildSchedule_Very_Short_Probation_Only_Produces_FinalDecision()
    {
        var expectedEndDate = StartDate.AddDays(10);

        var schedule = ProbationReviewScheduler.BuildSchedule(StartDate, expectedEndDate, [30, 60, 90]);

        var entry = Assert.Single(schedule);
        Assert.Equal(ProbationReviewType.FinalDecision, entry.ReviewType);
        Assert.Equal(expectedEndDate, entry.DueDate);
    }

    [Fact]
    public void BuildSchedule_Short_Probation_Only_First_Checkpoint_Survives()
    {
        // 40-day probation with default [30, 60, 90]: day 30 survives, 60/90 do not.
        var expectedEndDate = StartDate.AddDays(40);

        var schedule = ProbationReviewScheduler.BuildSchedule(StartDate, expectedEndDate, [30, 60, 90]);

        Assert.Equal(2, schedule.Count);
        Assert.Equal((ProbationReviewType.ManagerCheckIn, StartDate.AddDays(30)), schedule[0]);
        Assert.Equal((ProbationReviewType.FinalDecision, expectedEndDate), schedule[1]);
        Assert.DoesNotContain(schedule, e => e.ReviewType == ProbationReviewType.HrReview);
    }

    [Fact]
    public void BuildSchedule_Never_Produces_Numbered_Checkpoint_On_Or_After_EndDate()
    {
        // Checkpoint at exactly the end date offset must be skipped, not clamped.
        var expectedEndDate = StartDate.AddDays(30);

        var schedule = ProbationReviewScheduler.BuildSchedule(StartDate, expectedEndDate, [30, 60]);

        Assert.All(
            schedule.Where(e => e.ReviewType != ProbationReviewType.FinalDecision),
            e => Assert.True(e.DueDate < expectedEndDate));

        // Only FinalDecision remains since the sole checkpoint (30) equals the end date offset.
        var entry = Assert.Single(schedule);
        Assert.Equal(ProbationReviewType.FinalDecision, entry.ReviewType);
    }

    [Fact]
    public void BuildSchedule_More_Than_Two_Surviving_Checkpoints_Only_Uses_First_Two()
    {
        var expectedEndDate = StartDate.AddDays(365);

        var schedule = ProbationReviewScheduler.BuildSchedule(StartDate, expectedEndDate, [10, 20, 30, 40]);

        Assert.Equal(3, schedule.Count); // 2 checkpoints + FinalDecision
        Assert.Equal((ProbationReviewType.ManagerCheckIn, StartDate.AddDays(10)), schedule[0]);
        Assert.Equal((ProbationReviewType.HrReview, StartDate.AddDays(20)), schedule[1]);
        Assert.Equal((ProbationReviewType.FinalDecision, expectedEndDate), schedule[2]);
    }

    [Fact]
    public void BuildSchedule_Deduplicates_And_Ignores_NonPositive_Checkpoint_Days()
    {
        var expectedEndDate = StartDate.AddDays(365);

        var schedule = ProbationReviewScheduler.BuildSchedule(
            StartDate, expectedEndDate, [30, 30, 0, -5, 60, 60]);

        Assert.Equal(3, schedule.Count);
        Assert.Equal((ProbationReviewType.ManagerCheckIn, StartDate.AddDays(30)), schedule[0]);
        Assert.Equal((ProbationReviewType.HrReview, StartDate.AddDays(60)), schedule[1]);
        Assert.Equal((ProbationReviewType.FinalDecision, expectedEndDate), schedule[2]);
    }

    [Fact]
    public void BuildSchedule_Empty_Checkpoint_List_Only_Produces_FinalDecision()
    {
        var expectedEndDate = StartDate.AddDays(90);

        var schedule = ProbationReviewScheduler.BuildSchedule(StartDate, expectedEndDate, []);

        var entry = Assert.Single(schedule);
        Assert.Equal(ProbationReviewType.FinalDecision, entry.ReviewType);
        Assert.Equal(expectedEndDate, entry.DueDate);
    }
}
