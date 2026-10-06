namespace HR.Modules.Documents.Features.UploadEmployeeProfilePhoto;

internal sealed record UploadEmployeeProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string ScanStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
