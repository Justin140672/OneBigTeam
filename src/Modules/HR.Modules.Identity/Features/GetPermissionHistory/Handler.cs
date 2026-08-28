using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.GetPermissionHistory;

/// <summary>
/// IAM-08: company-wide permission-change history — direct role changes, position/inherited-role
/// changes and employee-level override changes surface together, reusing the same
/// IAuditHistoryReader-backed audit store every other module already writes to (see IdentityAudit.cs
/// and AddEmployeeRoleOverride/UpdateUserRoles/SetPositionRoleDefaults handlers), rather than a
/// second parallel history mechanism.
/// </summary>
internal sealed class GetPermissionHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IEmployeeNameReader employeeNameReader)
{
    /// <summary>
    /// Event types this view surfaces — every audit event IdentityAudit.cs publishes for a role,
    /// position-role-default or override change (direct role changes appear alongside
    /// position/override changes, per acceptance criteria), plus the account lifecycle events that
    /// change effective access indirectly.
    /// </summary>
    private static readonly HashSet<string> PermissionEventTypes =
    [
        "user.roles-changed",
        "user.role-change-rejected",
        "user.role-override-created",
        "user.role-override-removed",
        "user.role-override-expired",
        "position.role-defaults-changed",
        "employee.inherited-roles-recalculated",
        "user.disabled",
        "user.auto-disabled-offboarding",
        "user.enabled",
        "user.permission-denied",
    ];

    // Bounded fetch — company permission history is not expected to run into the tens of thousands
    // of rows; if it ever does, this should move to a DB-level entity-type filter on
    // IAuditHistoryReader rather than widening this cap.
    private const int FetchLimit = 5_000;

    public async Task<GetPermissionHistoryResponse> HandleAsync(GetPermissionHistoryRequest request, CancellationToken cancellationToken)
    {
        var actorFilter = request.ActorUserId is { } actorId ? new[] { actorId } : null;

        var page = await auditHistoryReader.GetPlatformAuditLogAsync(
            request.CompanyId,
            actorFilter,
            request.FromDate,
            request.ToDate,
            eventType: null,
            new Pagination(PageNumber: 1, PageSize: FetchLimit),
            cancellationToken);

        var entries = page.Items
            .Where(e => PermissionEventTypes.Contains(e.EventType))
            .Where(e => request.EmployeeId is null || e.EmployeeId == request.EmployeeId || e.EntityId == request.EmployeeId)
            .OrderByDescending(e => e.OccurredAt)
            .ToList();

        var actorIds = entries.Where(e => e.ActorUserId.HasValue).Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, actorIds, cancellationToken);

        var total = entries.Count;
        var pageItems = entries
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new PermissionHistoryItem(
                e.OccurredAt,
                e.EventType,
                string.IsNullOrEmpty(e.Summary) ? e.EventType : e.Summary,
                e.ActorUserId.HasValue ? names.GetValueOrDefault(e.ActorUserId.Value, "Unknown") : "System",
                e.EmployeeId,
                e.BeforeJson,
                e.AfterJson))
            .ToList();

        return new GetPermissionHistoryResponse(pageItems, total, request.Page, request.PageSize);
    }
}
