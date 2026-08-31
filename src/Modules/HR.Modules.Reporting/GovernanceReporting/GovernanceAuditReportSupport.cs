using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;

namespace HR.Modules.Reporting.GovernanceReporting;

/// <summary>
/// ADM-08: shared query support for the three audit-backed governance reports (User activity,
/// Administrative changes, Security events). Every one of these reports reads the central audit
/// store through <see cref="IAuditHistoryReader.GetCompanyAuditLogAsync"/> — the same tenant-scoped
/// audit query surface AUD-05 / GetCompanyAuditLog already use — rather than any competing record.
/// companyId isolation is enforced inside the reader; this helper only ever narrows the result set.
///
/// Coordination note: HR.Modules.Identity's in-progress Access &amp; Activity History work
/// (GetPermissionHistory) also consumes <see cref="IAuditHistoryReader"/>. Neither this helper nor
/// that feature extends the interface, so they compose without collision.
/// </summary>
internal sealed record GovernanceAuditRow(
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid? ActorUserId,
    string? ActorEmail,
    Guid? EmployeeId,
    string Status,
    string? Summary);

internal enum GovernanceAuditScope
{
    UserActivity,
    AdministrativeChanges,
    SecurityEvents,
}

internal static class GovernanceAuditReportSupport
{
    // event_type prefixes (lower-cased, '.'-delimited) that count as administrative configuration
    // / governance changes. Deliberately broad; the taxonomy is expected to be tuned alongside the
    // Audit &amp; Activity History tickets — see the ticket's "coordinate audit-query work" note.
    private static readonly string[] AdministrativeChangePrefixes =
    [
        "company.", "company-settings.", "companysettings.", "hr-settings.", "hrsettings.",
        "branding.", "settings.", "position.", "role.", "user.role", "user.roles",
        "user.permission", "user.disabled", "user.enabled", "user.invited", "user.invite",
        "leave-policy.", "leavepolicy.", "leave-type.", "document-type.", "documenttype.",
        "shared-document.", "sharedcompanydocument.", "onboarding-template.", "onboardingtemplate.",
        "recruitment-stage.", "recruitmentstage.", "report.", "subscription.",
    ];

    private static readonly string[] SecurityEventPrefixes =
    [
        "login.", "auth.", "session.", "support-session.", "supportsession.",
        "user.disabled", "user.enabled", "user.reactivated", "user.locked",
        "user.roles", "user.role-override", "user.roleoverride", "user.permission-denied",
        "user.permissiondenied", "user.invited", "user.invite",
        "role.assigned", "role.removed", "platform-administrator.", "platformadministrator.",
    ];

    public static bool MatchesScope(GovernanceAuditScope scope, string eventType, Guid? actorUserId)
    {
        var et = eventType.ToLowerInvariant();
        return scope switch
        {
            GovernanceAuditScope.UserActivity => actorUserId is not null,
            GovernanceAuditScope.AdministrativeChanges =>
                AdministrativeChangePrefixes.Any(p => et.StartsWith(p, StringComparison.Ordinal)),
            GovernanceAuditScope.SecurityEvents =>
                SecurityEventPrefixes.Any(p => et.StartsWith(p, StringComparison.Ordinal)),
            _ => false,
        };
    }

    public static string DeriveStatus(string eventType)
    {
        var et = eventType.ToLowerInvariant();
        return et.Contains("fail") || et.Contains("denied") || et.Contains("reject") || et.Contains("error")
            ? "Failed"
            : "Success";
    }

    public static readonly string[] ColumnHeaders =
        ["Occurred At (UTC)", "Event Type", "Entity Type", "Actor", "Status", "Summary"];

    public static IReadOnlyList<string?> ToExportRow(GovernanceAuditRow row) =>
        [
            row.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            row.EventType,
            row.EntityType,
            row.ActorEmail ?? row.ActorUserId?.ToString() ?? "System",
            row.Status,
            row.Summary,
        ];

    /// <summary>
    /// Runs the audit query for a governance report scope and applies the report's actor / event /
    /// employee / date / status filters. Returns the filtered rows (newest first), the total number
    /// of rows matching the report (used identically for on-screen paging and export, so exported
    /// data has the exact same scope as the on-screen report), and whether the underlying audit
    /// query hit its row cap.
    /// </summary>
    public static async Task<(IReadOnlyList<GovernanceAuditRow> Rows, int TotalCount, bool IsTruncated)> QueryAsync(
        IAuditHistoryReader auditHistoryReader,
        IUserEmailDirectoryReader userEmailDirectoryReader,
        GovernanceAuditScope scope,
        Guid companyId,
        Guid? actorUserId,
        string? eventType,
        Guid? employeeId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? status,
        CancellationToken cancellationToken)
    {
        var from = fromDate is { } f
            ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : (DateTimeOffset?)null;
        var to = toDate is { } t
            ? new DateTimeOffset(t.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero)
            : (DateTimeOffset?)null;

        var page = await auditHistoryReader.GetCompanyAuditLogAsync(
            companyId,
            employeeId,
            from,
            to,
            string.IsNullOrWhiteSpace(eventType) ? null : eventType,
            new Pagination(1, ReportLimits.ExportRowLimit),
            cancellationToken);

        var matched = page.Items
            .Where(e => MatchesScope(scope, e.EventType, e.ActorUserId))
            .Where(e => actorUserId is null || e.ActorUserId == actorUserId)
            .ToList();

        var actorIds = matched
            .Where(e => e.ActorUserId.HasValue)
            .Select(e => e.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var emails = actorIds.Count > 0
            ? await userEmailDirectoryReader.GetEmailsByUserIdsAsync(actorIds, cancellationToken)
            : new Dictionary<Guid, string>();

        var rows = matched
            .Select(e => new GovernanceAuditRow(
                e.OccurredAt,
                e.EventType,
                e.EntityType,
                e.ActorUserId,
                e.ActorUserId is { } id && emails.TryGetValue(id, out var email) ? email : null,
                e.EmployeeId,
                DeriveStatus(e.EventType),
                e.Summary))
            .Where(r => status is null || string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.OccurredAt)
            .ToList();

        var isTruncated = page.TotalCount >= ReportLimits.ExportRowLimit;
        return (rows, rows.Count, isTruncated);
    }
}
