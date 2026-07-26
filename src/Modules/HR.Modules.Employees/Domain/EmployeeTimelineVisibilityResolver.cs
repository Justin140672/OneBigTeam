namespace HR.Modules.Employees.Domain;

// Pure function implementing the three visibility tiers documented on EmployeeTimelineVisibility.
// Deliberately has no DI dependencies so it stays trivially unit-testable and reusable from both
// LINQ predicates (translated to SQL) and in-memory filtering.
internal static class EmployeeTimelineVisibilityResolver
{
    internal static bool CanView(
        EmployeeTimelineVisibility visibility,
        bool viewerIsHr,
        bool viewerIsSelf,
        bool viewerIsManager)
    {
        return visibility switch
        {
            EmployeeTimelineVisibility.HrOnly => viewerIsHr,
            EmployeeTimelineVisibility.EmployeeAndHr => viewerIsHr || viewerIsSelf,
            EmployeeTimelineVisibility.AuthorisedInternal => viewerIsHr || viewerIsSelf || viewerIsManager,
            _ => false,
        };
    }
}
