using HR.SharedKernel;

namespace HR.Modules.Recruitment;

// [P1] Candidate CV malware scanning — audit trail for every scan state transition, every retry and
// every quarantine. Published by ScanCandidateDocumentJob / ReconcileCandidateDocumentScansJob
// (background jobs: no human actor). Payloads carry identifiers, statuses, attempt numbers and a
// closed-set/sanitised reason only — never the file name, file content, storage key or raw exception
// text.
//
// EventId is derived deterministically from (document, attempt, outcome) so a job that re-publishes
// the same logical transition after a crash is deduplicated by the audit store's unique event_id.

/// <summary>A scan attempt finished (Clean, Infected or terminally Failed) or a lost attempt was
/// released by reconciliation.</summary>
internal sealed record CandidateDocumentScanStatusChangedAuditEvent(
    Guid CompanyId,
    Guid DocumentId,
    Guid CandidateId,
    string PreviousStatus,
    string NewStatus,
    int AttemptCount,
    string? Reason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid IAuditEvent.EventId => CandidateDocumentScanAuditEventIds.For(DocumentId, AttemptCount, $"status:{NewStatus}");
    AuditActorType IAuditEvent.ActorType => AuditActorType.ScheduledJob;
    string IAuditEvent.EventType => "candidate-document.scan-status-changed";
    string IAuditEvent.EntityType => "CandidateDocument";
    Guid IAuditEvent.EntityId => DocumentId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Candidate document malware scan (ScanCandidateDocumentJob): {PreviousStatus} -> {NewStatus}";
    object? IAuditEvent.Before => new { ScanStatus = PreviousStatus };
    object? IAuditEvent.After => new { ScanStatus = NewStatus, ScanAttemptCount = AttemptCount, Reason };
    object? IAuditEvent.Metadata => new { CandidateId };
}

/// <summary>A scan attempt failed (scanner or storage outage) and another bounded attempt has been
/// scheduled. The document stays Pending — i.e. not downloadable — in the meantime.</summary>
internal sealed record CandidateDocumentScanRetryScheduledAuditEvent(
    Guid CompanyId,
    Guid DocumentId,
    Guid CandidateId,
    int FailedAttempt,
    int MaxAttempts,
    string Reason,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid IAuditEvent.EventId => CandidateDocumentScanAuditEventIds.For(DocumentId, FailedAttempt, "retry");
    AuditActorType IAuditEvent.ActorType => AuditActorType.ScheduledJob;
    string IAuditEvent.EventType => "candidate-document.scan-retry-scheduled";
    string IAuditEvent.EntityType => "CandidateDocument";
    Guid IAuditEvent.EntityId => DocumentId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Candidate document malware scan attempt {FailedAttempt} of {MaxAttempts} failed; retry scheduled";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ScanStatus = "Pending", NextAttemptAt };
    object? IAuditEvent.Metadata => new { CandidateId, FailedAttempt, MaxAttempts, Reason };
}

/// <summary>An infected document's blob was handed to the durable deletion pipeline
/// (CandidateDocumentDeletionOperation). The document row is retained, marked Infected, as evidence.</summary>
internal sealed record CandidateDocumentQuarantinedAuditEvent(
    Guid CompanyId,
    Guid DocumentId,
    Guid CandidateId,
    Guid DeletionOperationId,
    string ThreatName,
    int AttemptCount,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid IAuditEvent.EventId => CandidateDocumentScanAuditEventIds.For(DocumentId, AttemptCount, "quarantined");
    AuditActorType IAuditEvent.ActorType => AuditActorType.ScheduledJob;
    string IAuditEvent.EventType => "candidate-document.quarantined";
    string IAuditEvent.EntityType => "CandidateDocument";
    Guid IAuditEvent.EntityId => DocumentId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Infected candidate document quarantined (ScanCandidateDocumentJob): stored file scheduled for durable deletion";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ScanStatus = "Infected", ThreatName };
    object? IAuditEvent.Metadata => new { CandidateId, DeletionOperationId };
}

internal static class CandidateDocumentScanAuditEventIds
{
    /// <summary>Deterministic, name-based id (RFC 4122 v5-style SHA-1 over the inputs).</summary>
    public static Guid For(Guid documentId, int attempt, string discriminator)
    {
        var input = System.Text.Encoding.UTF8.GetBytes($"candidate-document-scan:{documentId:N}:{attempt}:{discriminator}");
        var hash = System.Security.Cryptography.SHA1.HashData(input);
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5 (in .NET's little-endian Guid layout)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}
