namespace HR.Modules.Documents.Features.GetSharedCompanyDocument;

// The full management view — only reachable via the shared-document:manage policy. Deliberately
// includes fields (version history, audience, acknowledgement progress) that the employee-facing
// GetPublishedSharedCompanyDocument response never exposes.
internal sealed record GetSharedCompanyDocumentResponse(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    string FileName,
    long FileSize,
    string ContentType,
    int VersionNumber,
    string Status,
    DateOnly? EffectiveDate,
    DateOnly? ReviewDate,
    string ReviewFrequency,
    int? CustomReviewFrequencyMonths,
    Guid? ReviewOwnerEmployeeId,
    string? ReviewOwnerName,
    string AudienceDescription,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    IReadOnlyList<Guid> AudiencePositionProfileIds,
    IReadOnlyList<Guid> AudienceEmployeeIds,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    string? AcknowledgementStatement,
    AcknowledgementProgressInfo? AcknowledgementProgress,
    IReadOnlyList<SharedCompanyDocumentVersionItem> VersionHistory,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string UpdatedByName,
    DateTimeOffset UpdatedAt,
    string? PublishedByName,
    DateTimeOffset? PublishedAt,
    string? ArchivedByName,
    DateTimeOffset? ArchivedAt,
    string? ArchiveReason,
    DateOnly? LastReviewedAt,
    Guid? LastReviewedByEmployeeId,
    string? LastReviewedByName,
    string? LastReviewNotes,
    IReadOnlyList<SharedCompanyDocumentReviewHistoryItem> ReviewHistory);

internal sealed record AcknowledgementProgressInfo(
    int AcknowledgedCount,
    int EligibleCount,
    IReadOnlyList<string> AcknowledgedEmployeeNames);

internal sealed record SharedCompanyDocumentVersionItem(
    int VersionNumber,
    string FileName,
    long FileSize,
    string UploadedByName,
    DateTimeOffset UploadedAt,
    string? VersionNote,
    bool RequiresAcknowledgement,
    DateOnly? EffectiveDate,
    string PublicationStatus);

internal sealed record SharedCompanyDocumentReviewHistoryItem(
    DateOnly ReviewDate,
    Guid ReviewedByEmployeeId,
    string ReviewedByName,
    string? ReviewNotes,
    DateOnly? PreviousReviewDate);
