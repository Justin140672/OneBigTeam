namespace HR.Modules.Notifications.Domain;

/// <summary>
/// OBT-REM-14: durable per-company keyset cursor used by
/// <see cref="Jobs.ReconcileMissingNotificationAuditsJob"/> to make forward progress across runs.
///
/// Without this cursor the job re-selects the oldest <c>BatchSizePerCompany</c> notifications in the
/// lookback window on every run (ordered by CreatedAt). If those oldest candidates already have a
/// committed audit event, the job would skip them and repair nothing, and the exact same batch would
/// be re-selected on the next run forever — a genuinely missing audit further back in the window
/// would never be reached. Persisting the last-scanned (CreatedAt, NotificationId) position per
/// company lets each run resume where the previous one left off (keyset pagination, not offset), so
/// repeated executions always advance through the window instead of re-scanning the same rows.
///
/// One row per company keeps busy tenants from starving others — each company's scan progress is
/// independent, and a company with a large backlog cannot consume another company's batch budget.
/// </summary>
internal sealed class NotificationAuditReconciliationCursor
{
    private NotificationAuditReconciliationCursor() { }

    public Guid CompanyId { get; private set; }

    /// <summary>CreatedAt of the last notification scanned (not necessarily repaired) in the
    /// previous run, used as the keyset resume point.</summary>
    public DateTimeOffset LastScannedCreatedAt { get; private set; }

    /// <summary>Id of the last notification scanned in the previous run — the secondary keyset
    /// component, needed because CreatedAt is not unique.</summary>
    public Guid LastScannedNotificationId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static NotificationAuditReconciliationCursor Create(
        Guid companyId, DateTimeOffset lastScannedCreatedAt, Guid lastScannedNotificationId, DateTimeOffset now) => new()
    {
        CompanyId = companyId,
        LastScannedCreatedAt = lastScannedCreatedAt,
        LastScannedNotificationId = lastScannedNotificationId,
        UpdatedAt = now,
    };

    public void Advance(DateTimeOffset lastScannedCreatedAt, Guid lastScannedNotificationId, DateTimeOffset now)
    {
        LastScannedCreatedAt = lastScannedCreatedAt;
        LastScannedNotificationId = lastScannedNotificationId;
        UpdatedAt = now;
    }

    /// <summary>Resets progress so the next run restarts scanning from the beginning of the
    /// lookback window — used once a company's cursor has caught up to the end of the window (no
    /// more candidates ahead of it), so the window sliding forward over time is still covered.</summary>
    public void Reset(DateTimeOffset now)
    {
        LastScannedCreatedAt = default;
        LastScannedNotificationId = default;
        UpdatedAt = now;
    }
}
