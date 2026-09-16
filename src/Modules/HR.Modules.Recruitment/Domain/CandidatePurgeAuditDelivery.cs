using System.Text.Json;

namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Ticket 13 (P2): a durable record of a CandidatesPurgedAuditEvent owed by a purge batch, created
/// in the SAME transaction as the underlying candidate purge (see
/// Features/PurgeEligibleCandidates/Handler.cs). Previously the audit publish happened only inline,
/// after the purge had already committed — an inline publish failure (a transient fault in the
/// audit sink) silently dropped the audit trail for an otherwise fully-applied purge, with no record
/// left behind from which to recover it.
///
/// A recurring worker (see Jobs/PurgeCandidateDocumentStorageReconciliationJob.cs) claims Pending
/// deliveries and (re)publishes them — inline publish right after the same save remains a latency
/// optimisation only.
/// </summary>
internal sealed class CandidatePurgeAuditDelivery
{
    private CandidatePurgeAuditDelivery() { }

    public const string StatusPending   = "pending";
    public const string StatusDelivered = "delivered";

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PurgedBy { get; private set; }
    public string CandidateIdsJson { get; private set; } = "[]";
    public string Status { get; private set; } = StatusPending;
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    public IReadOnlyList<Guid> CandidateIds =>
        JsonSerializer.Deserialize<List<Guid>>(CandidateIdsJson) ?? [];

    public static CandidatePurgeAuditDelivery CreatePending(
        Guid id, Guid companyId, IReadOnlyList<Guid> candidateIds, Guid purgedBy, DateTimeOffset now)
    {
        return new CandidatePurgeAuditDelivery
        {
            Id = id,
            CompanyId = companyId,
            PurgedBy = purgedBy,
            CandidateIdsJson = JsonSerializer.Serialize(candidateIds),
            Status = StatusPending,
            AttemptCount = 0,
            CreatedAt = now,
        };
    }

    public void RecordAttempt() => AttemptCount++;

    public void MarkDelivered(DateTimeOffset now)
    {
        Status = StatusDelivered;
        DeliveredAt = now;
    }
}
