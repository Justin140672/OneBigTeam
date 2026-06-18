using HR.SharedKernel;

namespace HR.Modules.Documents;

internal sealed record DocumentExpiringSoonAuditEvent(
    Guid CompanyId,
    Guid EmployeeDocumentId,
    Guid EmployeeId,
    string Title,
    string DocumentTypeName,
    DateOnly ExpiryDate,
    int DaysUntilExpiry,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "document.expiring_soon";
    string  IAuditEvent.EntityType      => "EmployeeDocument";
    Guid    IAuditEvent.EntityId        => EmployeeDocumentId;
    Guid?   IAuditEvent.ActorUserId     => null;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' expires in {DaysUntilExpiry} day(s)";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => null;
    object? IAuditEvent.Metadata        => new { DocumentTypeName, EmployeeId, ExpiryDate, DaysUntilExpiry };
}

internal sealed record DocumentExpiredAuditEvent(
    Guid CompanyId,
    Guid EmployeeDocumentId,
    Guid EmployeeId,
    string Title,
    string DocumentTypeName,
    DateOnly ExpiryDate,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "document.expired";
    string  IAuditEvent.EntityType      => "EmployeeDocument";
    Guid    IAuditEvent.EntityId        => EmployeeDocumentId;
    Guid?   IAuditEvent.ActorUserId     => null;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' has expired";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => null;
    object? IAuditEvent.Metadata        => new { DocumentTypeName, EmployeeId, ExpiryDate };
}

internal sealed record DocumentDeletedAuditEvent(
    Guid CompanyId,
    Guid EmployeeDocumentId,
    Guid EmployeeId,
    string Title,
    string DocumentTypeName,
    string FileName,
    long FileSize,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    Guid DeletedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "document.deleted";
    string IAuditEvent.EntityType      => "EmployeeDocument";
    Guid   IAuditEvent.EntityId        => EmployeeDocumentId;
    Guid?  IAuditEvent.ActorUserId     => DeletedBy;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Document '{Title}' deleted";
    object? IAuditEvent.Before         => new { Title, DocumentTypeName, FileName, FileSize, IssueDate, ExpiryDate, EmployeeId };
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => null;
}

internal sealed record DocumentDownloadedAuditEvent(
    Guid CompanyId,
    Guid EmployeeDocumentId,
    Guid EmployeeId,
    string Title,
    string DocumentTypeName,
    string FileName,
    Guid DownloadedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "document.downloaded";
    string IAuditEvent.EntityType      => "EmployeeDocument";
    Guid   IAuditEvent.EntityId        => EmployeeDocumentId;
    Guid?  IAuditEvent.ActorUserId     => DownloadedBy;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Document '{Title}' downloaded";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => new { FileName, DocumentTypeName, EmployeeId };
}

internal sealed record DocumentUploadedAuditEvent(
    Guid CompanyId,
    Guid EmployeeDocumentId,
    Guid EmployeeId,
    string Title,
    string DocumentTypeName,
    string FileName,
    long FileSize,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    Guid UploadedBy,
    bool IsManagerUpload,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType    => "document.uploaded";
    string IAuditEvent.EntityType   => "EmployeeDocument";
    Guid   IAuditEvent.EntityId     => EmployeeDocumentId;
    Guid?  IAuditEvent.ActorUserId  => UploadedBy;
    Guid?  IAuditEvent.ActorEmployeeId => IsManagerUpload ? null : EmployeeId;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Document '{Title}' uploaded";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => new { Title, DocumentTypeName, FileName, FileSize, IssueDate, ExpiryDate, EmployeeId };
    object? IAuditEvent.Metadata       => new { IsManagerUpload };
}
