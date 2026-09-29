using HR.SharedKernel;

namespace HR.Modules.Recruitment;


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
    public static Guid For(Guid documentId, int attempt, string discriminator)
    {
        var input = System.Text.Encoding.UTF8.GetBytes($"candidate-document-scan:{documentId:N}:{attempt}:{discriminator}");
        var hash = System.Security.Cryptography.SHA1.HashData(input);
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
