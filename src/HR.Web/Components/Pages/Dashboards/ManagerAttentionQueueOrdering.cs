namespace HR.Web.Components.Pages.Dashboards;

/// <summary>
/// Pure aggregation/sort logic for <see cref="ManagerAttentionQueueWidget"/>, extracted so it can
/// be unit-tested without any Blazor/DI dependencies. Mirrors the sort approach used by
/// AttentionQueueWidget on the HR dashboard: overdue items first, then urgency rank, then soonest
/// due date (undated items sort last).
/// </summary>
public static class ManagerAttentionQueueOrdering
{
    /// <summary>
    /// A simple, Blazor-agnostic representation of one candidate row for the manager's attention
    /// queue. <typeparamref name="T"/> is the caller's richer item (e.g. carrying navigation
    /// delegates) — this class only needs the fields required to sort.
    /// </summary>
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

    /// <summary>Simple record form of a candidate row, for callers/tests that don't need a richer type.</summary>
    public sealed record Item(
        Guid? EmployeeId,
        string Category,
        DateOnly? DueDate,
        bool IsOverdue,
        int UrgencyRank);

    public static IReadOnlyList<Item> Order(IEnumerable<Item> items) =>
        Order(items, i => i.IsOverdue, i => i.UrgencyRank, i => i.DueDate);
}
