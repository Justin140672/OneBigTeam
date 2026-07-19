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
    Guid?   IAuditEvent.EmployeeId      => EmployeeId;
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
    Guid?   IAuditEvent.EmployeeId      => EmployeeId;
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
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
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
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => DownloadedBy;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Document '{Title}' downloaded";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => new { FileName, DocumentTypeName, EmployeeId };
}


internal sealed record SharedCompanyDocumentDownloadedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    string FileName,
    int VersionNumber,
    Guid DownloadedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.downloaded";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => DownloadedBy;
    Guid?   IAuditEvent.ActorEmployeeId => DownloadedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' downloaded";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => null;
    object? IAuditEvent.Metadata        => new { FileName, VersionNumber };
}

internal sealed record SharedCompanyDocumentMetadataUpdatedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    object Before,
    object After,
    Guid UpdatedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.metadata_updated";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => UpdatedBy;
    Guid?   IAuditEvent.ActorEmployeeId => UpdatedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' metadata updated";
    object? IAuditEvent.Before          => Before;
    object? IAuditEvent.After           => After;
    object? IAuditEvent.Metadata        => null;
}

internal sealed record SharedCompanyDocumentPublishedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    int VersionNumber,
    bool RequiresAcknowledgement,
    int AcknowledgementTasksCreated,
    Guid PublishedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.published";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => PublishedBy;
    Guid?   IAuditEvent.ActorEmployeeId => PublishedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' published";
    object? IAuditEvent.Before          => new { Status = "Draft" };
    object? IAuditEvent.After           => new { Status = "Published", VersionNumber };
    object? IAuditEvent.Metadata        => new { RequiresAcknowledgement, AcknowledgementTasksCreated };
}

internal sealed record SharedCompanyDocumentArchivedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    string Reason,
    int AcknowledgementTasksCancelled,
    Guid ArchivedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.archived";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => ArchivedBy;
    Guid?   IAuditEvent.ActorEmployeeId => ArchivedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' archived";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { Status = "Archived", Reason };
    object? IAuditEvent.Metadata        => new { AcknowledgementTasksCancelled };
}

internal sealed record SharedCompanyDocumentExpiredAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    int ReviewTasksCancelled,
    Guid ExpiredBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.expired";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => ExpiredBy;
    Guid?   IAuditEvent.ActorEmployeeId => ExpiredBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' expired";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { Status = "Expired" };
    object? IAuditEvent.Metadata        => new { ReviewTasksCancelled };
}

internal sealed record SharedCompanyDocumentReviewCompletedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    DateOnly? PreviousReviewDate,
    DateOnly ReviewDate,
    string? ReviewNotes,
    DateOnly? NextReviewDate,
    Guid ReviewedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.review_completed";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => ReviewedBy;
    Guid?   IAuditEvent.ActorEmployeeId => ReviewedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' review completed";
    object? IAuditEvent.Before          => new { ReviewDate = PreviousReviewDate };
    object? IAuditEvent.After           => new { ReviewDate, ReviewNotes, NextReviewDate };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record SharedCompanyDocumentAcknowledgementSettingsUpdatedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    object Before,
    object After,
    Guid UpdatedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.acknowledgement_settings_updated";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => UpdatedBy;
    Guid?   IAuditEvent.ActorEmployeeId => UpdatedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' acknowledgement settings changed";
    object? IAuditEvent.Before          => Before;
    object? IAuditEvent.After           => After;
    object? IAuditEvent.Metadata        => null;
}

internal sealed record SharedCompanyDocumentAudienceUpdatedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    string BeforeDescription,
    string AfterDescription,
    Guid UpdatedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.audience_updated";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => UpdatedBy;
    Guid?   IAuditEvent.ActorEmployeeId => UpdatedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' audience changed";
    object? IAuditEvent.Before          => new { Audience = BeforeDescription };
    object? IAuditEvent.After           => new { Audience = AfterDescription };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record SharedCompanyDocumentAcknowledgedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    int VersionNumber,
    Guid AcknowledgedBy,
    bool IsConfirmed,
    string AcknowledgementStatement,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.acknowledged";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => AcknowledgedBy;
    Guid?   IAuditEvent.ActorEmployeeId => AcknowledgedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' acknowledged (v{VersionNumber})";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { VersionNumber, IsConfirmed, AcknowledgementStatement };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record SharedCompanyDocumentCreatedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    Guid CategoryId,
    Guid CreatedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.created";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => CreatedBy;
    Guid?   IAuditEvent.ActorEmployeeId => CreatedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document '{Title}' created";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { Title, CategoryId, Status = "Draft" };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record SharedCompanyDocumentFileUploadedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    string FileName,
    long FileSize,
    int VersionNumber,
    Guid UploadedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.file_uploaded";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => UploadedBy;
    Guid?   IAuditEvent.ActorEmployeeId => UploadedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"File uploaded for document '{Title}'";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { FileName, FileSize, VersionNumber };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record SharedCompanyDocumentVersionUploadedAuditEvent(
    Guid CompanyId,
    Guid SharedCompanyDocumentId,
    string Title,
    string FileName,
    long FileSize,
    int VersionNumber,
    string VersionNote,
    bool RequiresReacknowledgement,
    Guid UploadedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "shared_company_document.version_uploaded";
    string  IAuditEvent.EntityType      => "SharedCompanyDocument";
    Guid    IAuditEvent.EntityId        => SharedCompanyDocumentId;
    Guid?   IAuditEvent.EmployeeId      => null;
    Guid?   IAuditEvent.ActorUserId     => UploadedBy;
    Guid?   IAuditEvent.ActorEmployeeId => UploadedBy;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"New version uploaded for document '{Title}' (v{VersionNumber})";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { FileName, FileSize, VersionNumber, VersionNote, RequiresReacknowledgement };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record DocumentRequestFulfilledAuditEvent(
    Guid CompanyId,
    Guid DocumentRequestId,
    Guid EmployeeId,
    string DocumentTypeName,
    Guid FulfilledBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "document_request.fulfilled";
    string IAuditEvent.EntityType      => "DocumentRequest";
    Guid   IAuditEvent.EntityId        => DocumentRequestId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => FulfilledBy;
    Guid?  IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Document request for '{DocumentTypeName}' fulfilled";
    object? IAuditEvent.Before         => new { Status = "Requested" };
    object? IAuditEvent.After          => new { Status = "Uploaded", FulfilledBy };
    object? IAuditEvent.Metadata       => null;
}

internal sealed record DocumentRequestCancelledAuditEvent(
    Guid CompanyId,
    Guid DocumentRequestId,
    Guid EmployeeId,
    string DocumentTypeName,
    Guid CancelledBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "document_request.cancelled";
    string IAuditEvent.EntityType      => "DocumentRequest";
    Guid   IAuditEvent.EntityId        => DocumentRequestId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => CancelledBy;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Document request for '{DocumentTypeName}' cancelled";
    object? IAuditEvent.Before         => new { Status = "Requested" };
    object? IAuditEvent.After          => new { Status = "Cancelled", CancelledBy };
    object? IAuditEvent.Metadata       => null;
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
    Guid?  IAuditEvent.EmployeeId   => EmployeeId;
    Guid?  IAuditEvent.ActorUserId  => UploadedBy;
    Guid?  IAuditEvent.ActorEmployeeId => IsManagerUpload ? null : EmployeeId;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Document '{Title}' uploaded";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => new { Title, DocumentTypeName, FileName, FileSize, IssueDate, ExpiryDate, EmployeeId };
    object? IAuditEvent.Metadata       => new { IsManagerUpload };
}

internal sealed record ProfilePhotoUploadedAuditEvent(
    Guid CompanyId,
    Guid EmployeeProfilePhotoId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    Guid UploadedBy,
    bool IsManagerUpload,
    bool IsReplace,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "profile_photo.uploaded";
    string IAuditEvent.EntityType      => "EmployeeProfilePhoto";
    Guid   IAuditEvent.EntityId        => EmployeeProfilePhotoId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => UploadedBy;
    Guid?  IAuditEvent.ActorEmployeeId => IsManagerUpload ? null : EmployeeId;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => IsReplace ? $"Profile photo replaced for employee '{EmployeeId}'" : $"Profile photo uploaded for employee '{EmployeeId}'";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => new { FileName, FileSize, EmployeeId };
    object? IAuditEvent.Metadata       => new { IsManagerUpload, IsReplace };
}

internal sealed record ProfilePhotoSubmittedAuditEvent(
    Guid CompanyId,
    Guid PendingProfilePhotoId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    Guid SubmittedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "profile_photo.submitted";
    string  IAuditEvent.EntityType      => "PendingProfilePhoto";
    Guid    IAuditEvent.EntityId        => PendingProfilePhotoId;
    Guid?   IAuditEvent.EmployeeId      => EmployeeId;
    Guid?   IAuditEvent.ActorUserId     => SubmittedBy;
    Guid?   IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Profile photo submitted for review for employee '{EmployeeId}'";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { FileName, FileSize, EmployeeId };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record ProfilePhotoCancelledAuditEvent(
    Guid CompanyId,
    Guid PendingProfilePhotoId,
    Guid EmployeeId,
    Guid CancelledBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "profile_photo.cancelled";
    string  IAuditEvent.EntityType      => "PendingProfilePhoto";
    Guid    IAuditEvent.EntityId        => PendingProfilePhotoId;
    Guid?   IAuditEvent.EmployeeId      => EmployeeId;
    Guid?   IAuditEvent.ActorUserId     => CancelledBy;
    Guid?   IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Pending profile photo submission cancelled for employee '{EmployeeId}'";
    object? IAuditEvent.Before          => new { Status = "Pending" };
    object? IAuditEvent.After           => new { Status = "Cancelled" };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record ProfilePhotoApprovedAuditEvent(
    Guid CompanyId,
    Guid EmployeeProfilePhotoId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    Guid ReviewedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "profile_photo.approved";
    string  IAuditEvent.EntityType      => "EmployeeProfilePhoto";
    Guid    IAuditEvent.EntityId        => EmployeeProfilePhotoId;
    Guid?   IAuditEvent.EmployeeId      => EmployeeId;
    Guid?   IAuditEvent.ActorUserId     => ReviewedBy;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Profile photo approved for employee '{EmployeeId}'";
    object? IAuditEvent.Before          => new { Status = "Pending" };
    object? IAuditEvent.After           => new { Status = "Approved", FileName, FileSize };
    object? IAuditEvent.Metadata        => new { ReviewedBy };
}

internal sealed record ProfilePhotoRejectedAuditEvent(
    Guid CompanyId,
    Guid PendingProfilePhotoId,
    Guid EmployeeId,
    Guid ReviewedBy,
    string? RejectionReason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "profile_photo.rejected";
    string  IAuditEvent.EntityType      => "PendingProfilePhoto";
    Guid    IAuditEvent.EntityId        => PendingProfilePhotoId;
    Guid?   IAuditEvent.EmployeeId      => EmployeeId;
    Guid?   IAuditEvent.ActorUserId     => ReviewedBy;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Profile photo rejected for employee '{EmployeeId}'";
    object? IAuditEvent.Before          => new { Status = "Pending" };
    object? IAuditEvent.After           => new { Status = "Rejected", RejectionReason };
    object? IAuditEvent.Metadata        => new { ReviewedBy, RejectionReason };
}
