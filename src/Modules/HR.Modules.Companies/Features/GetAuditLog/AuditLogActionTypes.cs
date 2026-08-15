namespace HR.Modules.Companies.Features.GetAuditLog;

/// <summary>
/// Curated, explicit list of the IAuditEvent.EventType values every existing platform-administrator
/// action writes (Subscription Management, Support, Job Monitoring epics — see each Audit.cs record
/// for the canonical source of each string). Deliberately a static list rather than a distinct-query
/// against audit rows: matches the "explicit All actions item, not a discovered/free-text list"
/// dropdown convention already used elsewhere (e.g. GetFailedPaymentsRequest.StatusFilter), and
/// guarantees a stable, complete set even for an empty/near-empty audit table.
/// </summary>
internal static class AuditLogActionTypes
{
    public const string TrialExtended = "subscription.trial-extended";
    public const string AdminForcedReadOnly = "subscription.admin-forced-read-only";
    public const string AdminResumedService = "subscription.admin-resumed-service";
    public const string AdminCancelled = "subscription.admin-cancelled";
    public const string AdminReinstated = "subscription.admin-reinstated";
    public const string SupportSessionGenerated = "support.session-generated";
    public const string SupportSessionRedeemed = "support.session-redeemed";
    public const string SupportSessionRevoked = "support.session-revoked";
    public const string BackgroundJobAdminRetried = "background-job.admin-retried";

    public static readonly IReadOnlyList<string> All =
    [
        TrialExtended,
        AdminForcedReadOnly,
        AdminResumedService,
        AdminCancelled,
        AdminReinstated,
        SupportSessionGenerated,
        SupportSessionRedeemed,
        SupportSessionRevoked,
        BackgroundJobAdminRetried,
    ];
}
