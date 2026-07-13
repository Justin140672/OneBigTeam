namespace HR.Modules.Employees.Features.GetRecentEmployeeChanges;

internal sealed record RecentEmployeeChangeItem(
    DateTimeOffset OccurredAt,
    string EmployeeName,
    string Action,
    string ActorName);

internal sealed record GetRecentEmployeeChangesResponse(IReadOnlyList<RecentEmployeeChangeItem> Items);
