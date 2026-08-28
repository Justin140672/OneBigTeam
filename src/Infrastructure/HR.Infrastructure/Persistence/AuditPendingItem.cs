using System.Text.Json;
using HR.SharedKernel;

namespace HR.Infrastructure.Persistence;

/// <summary>
/// AUD-01: durable staging record written atomically with the business change.
/// A Hangfire job promotes pending items to <see cref="AuditEvent"/> once they are observed.
/// Using a separate staging table keeps the promotion idempotent — if promotion fails, the
/// pending row remains and will be retried; if it succeeds more than once, the unique
/// constraint on <see cref="AuditEvent.EventId"/> prevents duplicates.
/// </summary>
internal sealed class AuditPendingItem
{
    private AuditPendingItem() { }

    public const string StatusPending    = "pending";
    public const string StatusProcessing = "processing";
    public const string StatusCommitted  = "committed";
    public const string StatusFailed     = "failed";

    public Guid   Id           { get; private set; }
    public Guid   EventId      { get; private set; }
    public string PayloadJson  { get; private set; } = string.Empty;
    public string Status       { get; private set; } = StatusPending;
    public int    AttemptCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt    { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    internal static AuditPendingItem From(IAuditEvent evt)
    {
        var payload = new PendingAuditPayload(
            evt.EventId,
            evt.CompanyId,
            evt.EventType,
            evt.EntityType,
            evt.EntityId,
            evt.EmployeeId,
            evt.ActorUserId,
            evt.ActorEmployeeId,
            evt.OccurredAt,
            evt.CorrelationId,
            evt.Summary,
            evt.Before  is null ? null : JsonSerializer.Serialize(evt.Before),
            evt.After   is null ? null : JsonSerializer.Serialize(evt.After),
            evt.Metadata is null ? null : JsonSerializer.Serialize(evt.Metadata));

        return new AuditPendingItem
        {
            Id          = Guid.NewGuid(),
            EventId     = evt.EventId,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status      = StatusPending,
            CreatedAt   = evt.OccurredAt,
        };
    }

    public void MarkProcessing()
    {
        Status = StatusProcessing;
        AttemptCount++;
        ErrorMessage = null;
    }

    public void MarkCommitted(DateTimeOffset now)
    {
        Status      = StatusCommitted;
        ProcessedAt = now;
    }

    public void MarkFailed(string reason)
    {
        Status       = StatusFailed;
        ErrorMessage = reason.Length > 2000 ? reason[..2000] : reason;
    }

    /// <summary>Resets a Failed item back to Pending so the background job will retry it.</summary>
    public void ResetForRetry()
    {
        if (Status != StatusFailed)
            throw new InvalidOperationException($"Cannot retry an audit pending item with status '{Status}'.");
        Status       = StatusPending;
        ErrorMessage = null;
    }
}

/// <summary>Serialised snapshot of an <see cref="IAuditEvent"/> used as the pending-item payload.</summary>
internal sealed record PendingAuditPayload(
    Guid           EventId,
    Guid           CompanyId,
    string         EventType,
    string         EntityType,
    Guid           EntityId,
    Guid?          EmployeeId,
    Guid?          ActorUserId,
    Guid?          ActorEmployeeId,
    DateTimeOffset OccurredAt,
    Guid?          CorrelationId,
    string?        Summary,
    string?        BeforeJson,
    string?        AfterJson,
    string?        MetadataJson);
