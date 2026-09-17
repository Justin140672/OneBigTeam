using HR.Web.Components.Pages.Dashboards;

namespace HR.Web.Tests;

/// <summary>
/// Regression coverage proving that <see cref="ManagerAttentionQueueOrdering.Order{T}"/>, called with
/// the exact same selectors the HR widget uses
/// (<c>i => i.IsOverdue, i => i.UrgencyRank, i => i.DueDate</c>), sorts <see cref="AttentionQueueItem"/>
/// rows correctly. This pins AttentionQueueWidget.razor's ordering call — the manager widget's
/// equivalent call is already covered via <see cref="ManagerAttentionQueueOrdering.Item"/> in
/// ManagerAttentionQueueOrderingTests.cs.
/// </summary>
public class AttentionQueueItemOrderingTests
{
    private static AttentionQueueItem Item(
        bool isOverdue,
        int urgencyRank,
        DateOnly? dueDate,
        string actionTitle = "Row") =>
        new(
            EmployeeId: Guid.NewGuid(),
            ActionTitle: actionTitle,
            EmployeeName: "Jane Doe",
            Category: "Task",
            StatusLabel: "Pending",
            DueDate: dueDate,
            DueLabel: null,
            DueCss: "normal",
            IsOverdue: isOverdue,
            UrgencyRank: urgencyRank,
            TaskId: null,
            DeepLinkUrl: "/tasks/1");

    private static IReadOnlyList<AttentionQueueItem> OrderHrStyle(IEnumerable<AttentionQueueItem> items) =>
        ManagerAttentionQueueOrdering.Order(
            items,
            isOverdue: i => i.IsOverdue,
            urgencyRank: i => i.UrgencyRank,
            dueDate: i => i.DueDate);

    [Fact]
    public void HrOrdering_OverdueSortsBeforeNonOverdue()
    {
        var overdue = Item(isOverdue: true, urgencyRank: 99, dueDate: new DateOnly(2026, 12, 31), actionTitle: "Overdue");
        var notOverdue = Item(isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 1, 1), actionTitle: "NotOverdue");

        var result = OrderHrStyle([notOverdue, overdue]);

        Assert.Equal([overdue, notOverdue], result);
    }

    [Fact]
    public void HrOrdering_SameOverdueStatus_LowerUrgencyRankSortsFirst()
    {
        var high = Item(isOverdue: false, urgencyRank: 1, dueDate: null, actionTitle: "High");
        var low = Item(isOverdue: false, urgencyRank: 5, dueDate: null, actionTitle: "Low");

        var result = OrderHrStyle([low, high]);

        Assert.Equal([high, low], result);
    }

    [Fact]
    public void HrOrdering_SameOverdueAndUrgency_SoonestDueDateSortsFirst()
    {
        var soon = Item(isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 1, 1), actionTitle: "Soon");
        var later = Item(isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 6, 1), actionTitle: "Later");

        var result = OrderHrStyle([later, soon]);

        Assert.Equal([soon, later], result);
    }

    [Fact]
    public void HrOrdering_UndatedItemsSortLast_WithinSameOverdueAndUrgencyGroup()
    {
        var dated = Item(isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2099, 12, 31), actionTitle: "Dated");
        var undated = Item(isOverdue: false, urgencyRank: 1, dueDate: null, actionTitle: "Undated");

        var result = OrderHrStyle([undated, dated]);

        Assert.Equal([dated, undated], result);
    }

    [Fact]
    public void HrOrdering_FullPrecedence_Overdue_ThenUrgency_ThenDueDate_ThenUndatedLast()
    {
        var overdue = Item(isOverdue: true, urgencyRank: 3, dueDate: null, actionTitle: "Overdue");
        var urgent = Item(isOverdue: false, urgencyRank: 1, dueDate: new DateOnly(2026, 3, 1), actionTitle: "Urgent");
        var lessUrgentSoon = Item(isOverdue: false, urgencyRank: 2, dueDate: new DateOnly(2026, 1, 1), actionTitle: "LessUrgentSoon");
        var lessUrgentUndated = Item(isOverdue: false, urgencyRank: 2, dueDate: null, actionTitle: "LessUrgentUndated");

        var result = OrderHrStyle([lessUrgentUndated, lessUrgentSoon, urgent, overdue]);

        Assert.Equal([overdue, urgent, lessUrgentSoon, lessUrgentUndated], result);
    }
}
