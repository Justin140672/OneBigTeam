namespace HR.Web.Components.Pages.Dashboards;

public static class ManagerAttentionQueueOrdering
{
    public static IReadOnlyList<T> Order<T>(
        IEnumerable<T> items,
        Func<T, bool> isOverdue,
        Func<T, int> urgencyRank,
        Func<T, DateOnly?> dueDate)
    {
        return items
            .OrderByDescending(isOverdue)
            .ThenBy(urgencyRank)
            .ThenBy(i => dueDate(i) ?? DateOnly.MaxValue)
            .ToList();
    }

    public sealed record Item(
        Guid? EmployeeId,
        string Category,
        DateOnly? DueDate,
        bool IsOverdue,
        int UrgencyRank);

    public static IReadOnlyList<Item> Order(IEnumerable<Item> items) =>
        Order(items, i => i.IsOverdue, i => i.UrgencyRank, i => i.DueDate);
}
