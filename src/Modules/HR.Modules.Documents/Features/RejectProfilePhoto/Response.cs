namespace HR.Modules.Documents.Features.RejectProfilePhoto;

internal sealed record RejectProfilePhotoResponse(
    Guid PendingProfilePhotoId,
    Guid EmployeeId,
    string? RejectionReason,
    Guid ReviewedBy,
    DateTimeOffset ReviewedAt);
