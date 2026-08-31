using System.Security.Claims;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Implemented once per outstanding-action category, in the module that owns the underlying
/// business capability (OBT-721, Workload &amp; HR Actions Report). Mirrors how
/// <see cref="ITaskCompletionAction"/> implementations live per-module and are fanned out via DI —
/// here HR.Modules.Reporting resolves every registered <see cref="IWorkloadActionProvider"/> and
/// merges their results into a single cross-module workload dashboard, without ever referencing
/// the owning modules directly.
///
/// Security is the reason this interface takes the caller's <see cref="ClaimsPrincipal"/> rather
/// than a pre-computed "is HR" flag: each provider MUST self-enforce its own row-level scoping
/// (manager sees only direct reports, HR sees company-wide, etc.) exactly the way
/// GetProbationReport/Handler.cs and GetLeaveSummaryReport/Handler.cs already do for their single
/// report. The Reporting aggregation endpoint never re-derives or duplicates that scoping logic —
/// it only merges what each provider already decided the caller is allowed to see. A provider that
/// returns company-wide data to a non-HR caller is a direct tenant-isolation/authorization bug in
/// that provider, not something the aggregator can catch after the fact.
/// </summary>
public interface IWorkloadActionProvider
{
    /// <summary>
    /// Display name for this provider's category, e.g. "Pending Leave Approvals",
    /// "Overdue Probation Reviews". Shown as the ActionCategory on every action it returns and
    /// used for category-based grouping/filtering on the aggregation endpoint.
    /// </summary>
    string ActionCategory { get; }

    /// <summary>
    /// Returns the outstanding actions in this category that <paramref name="caller"/> is allowed
    /// to see for <paramref name="companyId"/>. Implementations must:
    /// 1. Resolve the caller's roles/employee id from <paramref name="caller"/> (via
    ///    IAuthorizationService policy checks and the "sub" claim, same pattern used by
    ///    GetProbationReport/Endpoint.cs) — never trust anything client-supplied.
    /// 2. Apply their own row-level scoping (HR-only, manager-scoped to direct reports,
    ///    recruitment-scoped, or self-scoped) before returning anything.
    /// 3. Return an empty list rather than throwing when the caller has no matching role/scope —
    ///    a 403 for the whole report is the aggregation endpoint's job (baseline reporting:view
    ///    policy), not an individual provider's.
    /// </summary>
    Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken);
}

/// <summary>
/// Urgency bucket for a <see cref="WorkloadAction"/>, computed centrally by the Reporting
/// aggregation handler from DueDate against "today" so every provider's output is judged against
/// the same clock rather than each provider computing it independently.
/// </summary>
public enum WorkloadActionUrgency
{
    Overdue,
    DueToday,
    DueThisWeek,
    Upcoming
}

/// <summary>
/// A single outstanding people-related action surfaced on the Workload &amp; HR Actions Report.
/// </summary>
/// <param name="EmployeeId">The employee the action relates to (subject, not necessarily assignee).</param>
/// <param name="EmployeeName">Display name for EmployeeId, resolved by the owning module.</param>
/// <param name="Department">Department name for EmployeeId, if known.</param>
/// <param name="ActionType">Specific action, e.g. "Approve Leave Request", "Complete Review".</param>
/// <param name="ActionCategory">The owning provider's <see cref="IWorkloadActionProvider.ActionCategory"/>.</param>
/// <param name="DueDate">When the action is due, if applicable.</param>
/// <param name="AssignedTo">Display name of whoever the action is assigned to/owned by, if applicable.</param>
/// <param name="Status">Free-text status label, e.g. "Pending", "Overdue".</param>
/// <param name="DeepLinkUrl">Relative URL into the screen where the action can actually be actioned.</param>
/// <param name="Urgency">Populated by the aggregation handler; providers may leave this at the default.</param>
/// <param name="TaskId">
/// The owning module's local TaskItem id for this action, when it already has one to hand in its own
/// schema (e.g. the Tasks module's own overdue-task providers). Left null when no local task id
/// exists — consumers fall back to <paramref name="DeepLinkUrl"/> navigation. Never populated via a
/// cross-module join. Additive/optional (DSH-06).
/// </param>
public sealed record WorkloadAction(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string ActionType,
    string ActionCategory,
    DateOnly? DueDate,
    string? AssignedTo,
    string Status,
    string DeepLinkUrl,
    WorkloadActionUrgency Urgency = WorkloadActionUrgency.Upcoming,
    Guid? TaskId = null)
{
    public static WorkloadActionUrgency ComputeUrgency(DateOnly? dueDate, DateOnly today)
    {
        if (dueDate is null)
            return WorkloadActionUrgency.Upcoming;

        if (dueDate < today)
            return WorkloadActionUrgency.Overdue;

        if (dueDate == today)
            return WorkloadActionUrgency.DueToday;

        return dueDate <= today.AddDays(7)
            ? WorkloadActionUrgency.DueThisWeek
            : WorkloadActionUrgency.Upcoming;
    }
}
