using HR.Web.Components.Pages.Dashboards;

namespace HR.Web.Tests;

public class ManagerAttentionQueueOrderingTests
{
    private static ManagerAttentionQueueOrdering.Item Item(
        string category,
        bool isOverdue,
        int urgencyRank,
        DateOnly? dueDate) =>
        new(Guid.NewGuid(), category, dueDate, isOverdue, urgencyRank);

    [Fact]
    public void Order_Empty_Input_Returns_Empty_Non_Null_List()
    {
        var result = ManagerAttentionQueueOrdering.Order([]);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Order_Overdue_Items_Sort_Before_Non_Overdue_Regardless_Of_Urgency_Or_Due_Date()
    {
        var overdue = Item("Task", isOverdue: true, urgencyRank: 100, dueDate: new DateOnly(2026, 12, 31));
        var notOverdue = Item("LeaveRequest", isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 1, 1));

        var result = ManagerAttentionQueueOrdering.Order([notOverdue, overdue]);

        Assert.Equal([overdue, notOverdue], result);
    }

    [Fact]
    public void Order_Same_Overdue_Status_Lower_Urgency_Rank_Sorts_First()
    {
        var lowUrgency = Item("Task", isOverdue: false, urgencyRank: 5, dueDate: null);
        var highUrgency = Item("Task", isOverdue: false, urgencyRank: 1, dueDate: null);

        var result = ManagerAttentionQueueOrdering.Order([lowUrgency, highUrgency]);

        Assert.Equal([highUrgency, lowUrgency], result);
    }

    [Fact]
    public void Order_Same_Overdue_And_Urgency_Rank_Earlier_Due_Date_Sorts_First()
    {
        var later = Item("Task", isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 6, 1));
        var earlier = Item("Task", isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 1, 1));

        var result = ManagerAttentionQueueOrdering.Order([later, earlier]);

        Assert.Equal([earlier, later], result);
    }

    [Fact]
    public void Order_Null_Due_Date_Sorts_After_Items_With_Due_Date_In_Same_Group()
    {
        var noDate = Item("Task", isOverdue: false, urgencyRank: 1, dueDate: null);
        var withDate = Item("Task", isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2099, 12, 31));

        var result = ManagerAttentionQueueOrdering.Order([noDate, withDate]);

        Assert.Equal([withDate, noDate], result);
    }

    [Fact]
    public void Order_Mixed_Realistic_Scenario_Produces_Fully_Expected_Order()
    {
        // Overdue return-to-work review, most overdue-relevant category, urgency rank 1, undated.
        var overdueReturnToWork = Item("ReturnToWorkReview", isOverdue: true, urgencyRank: 1, dueDate: null);
        // Overdue task, urgency rank 2.
        var overdueTask = Item("Task", isOverdue: true, urgencyRank: 2, dueDate: new DateOnly(2026, 8, 1));
        // Overdue task, urgency rank 2, but due later than the one above -> sorts after it.
        var overdueTaskLater = Item("Task", isOverdue: true, urgencyRank: 2, dueDate: new DateOnly(2026, 8, 10));
        // Not overdue: missing fit note, urgency rank 1, due soon.
        var missingFitNote = Item("MissingFitNote", isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 9, 1));
        // Not overdue: probation review, urgency rank 1, due later than fit note.
        var probationReview = Item("ProbationReview", isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 9, 15));
        // Not overdue: pending leave request, urgency rank 2, undated.
        var pendingLeaveRequest = Item("LeaveRequest", isOverdue: false, urgencyRank: 2, dueDate: null);

        var input = new[]
        {
            pendingLeaveRequest,
            probationReview,
            overdueTaskLater,
            missingFitNote,
            overdueReturnToWork,
            overdueTask,
        };

        var result = ManagerAttentionQueueOrdering.Order(input);

        Assert.Equal(
            [
                overdueReturnToWork,
                overdueTask,
                overdueTaskLater,
                missingFitNote,
                probationReview,
                pendingLeaveRequest,
            ],
            result);
    }

    private sealed record CustomWidget(string Name, bool Late, int Priority, DateOnly? Deadline);

    [Fact]
    public void Order_Generic_Overload_Works_With_Custom_Type()
    {
        var a = new CustomWidget("A", Late: true, Priority: 5, Deadline: new DateOnly(2026, 1, 1));
        var b = new CustomWidget("B", Late: false, Priority: 1, Deadline: new DateOnly(2026, 1, 1));
        var c = new CustomWidget("C", Late: true, Priority: 1, Deadline: new DateOnly(2026, 3, 1));
        var d = new CustomWidget("D", Late: true, Priority: 1, Deadline: new DateOnly(2026, 2, 1));

        var result = ManagerAttentionQueueOrdering.Order(
            [a, b, c, d],
            isOverdue: w => w.Late,
            urgencyRank: w => w.Priority,
            dueDate: w => w.Deadline);

        Assert.Equal([d, c, a, b], result);
    }
}
