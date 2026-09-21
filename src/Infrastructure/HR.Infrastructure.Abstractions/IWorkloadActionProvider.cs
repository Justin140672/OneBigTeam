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
/// <summary>
/// Explicit workspace the caller is currently viewing. Introduced to fix a role-bleed bug where a
/// user holding BOTH the HR and Manager roles saw HR-only, company-wide data on their Manager
/// dashboard/attention queue: providers used to re-derive "am I HR" from the caller alone, so an
/// HR+Manager caller always got the HR (company-wide) branch, even when the request came in through
/// the Manager-scoped route. The caller of <see cref="IWorkloadActionProvider.GetActionsAsync"/> now
/// states which workspace it is composing for; providers must honour that explicitly rather than
/// re-inferring it, and must still re-verify the caller is actually authorized for that workspace
/// (a requested workspace must never grant access the caller doesn't otherwise have).
/// </summary>
public enum WorkloadScope
{
    /// <summary>
    /// The caller's Manager workspace: only items assigned via manager responsibility or visible
    /// under the caller's own reporting sub-tree (direct/indirect reports). Never company-wide, even
    /// when the caller also holds an HR role.
    /// </summary>
    Manager,

    /// <summary>
    /// The caller's HR workspace: HR queue items and company-wide data visible under HR rules.
    /// Requires the caller to hold HR access; providers must return an empty list rather than
    /// company-wide data for a caller who lacks it, regardless of this requested scope.
    /// </summary>
    Hr
}

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
    /// to see for <paramref name="companyId"/> within the explicitly requested
    /// <paramref name="requestedScope"/> workspace. Implementations must:
    /// 1. Resolve the caller's roles/employee id from <paramref name="caller"/> (via
    ///    IAuthorizationService policy checks and the "sub" claim, same pattern used by
    ///    GetProbationReport/Endpoint.cs) — never trust anything client-supplied.
    /// 2. Apply <paramref name="requestedScope"/>'s inclusion rules (HR-only/company-wide items only
    ///    for <see cref="WorkloadScope.Hr"/>; manager-assignment/reporting-sub-tree items only for
    ///    <see cref="WorkloadScope.Manager"/>) — never widen scope based on the caller ALSO holding a
    ///    different role. A caller with both HR and Manager roles requesting
    ///    <see cref="WorkloadScope.Manager"/> must still only see their own team's items.
    /// 3. Re-verify the caller is actually authorized for the requested scope (e.g. HR scope
    ///    requires the caller to hold reporting:view-hr) — a requested scope is a display-routing
    ///    signal only, never an authorization escalation path.
    /// 4. Return an empty list rather than throwing when the caller has no matching role/scope —
    ///    a 403 for the whole report is the aggregation endpoint's job (baseline reporting:view
    ///    policy), not an individual provider's.
    /// </summary>
    Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
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
/// <param name="IsOwnerActionable">
/// Whether this action is actionable by the caller in the <see cref="WorkloadScope"/> workspace it
/// was requested for — distinct from mere visibility. A provider may legitimately surface an item
/// for oversight (e.g. HR viewing a pending leave approval that is actually assigned to the
/// employee's manager) without it being something the current viewer should be able to click into
/// and act on from that list. Defaults to true (actionable) so existing providers are unaffected;
/// a provider must explicitly set this to false when the true task owner differs from the
/// workspace being composed for. Consumers must never infer actionability from visibility alone,
/// and must never grant it purely because the caller holds a role (e.g. HR) that a server-side
/// override would separately allow — this flag governs presentation only, never authorization.
/// </param>
/// <param name="OwnerLabel">
/// Human-readable owner/responsibility label shown when <see cref="IsOwnerActionable"/> is false,
/// e.g. "Owned by the employee's manager" — lets a read-only row explain who this action actually
/// belongs to rather than implying it is stale/missing.
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
    Guid? TaskId = null,
    bool IsOwnerActionable = true,
    string? OwnerLabel = null)
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
