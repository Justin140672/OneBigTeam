namespace HR.Modules.Documents.Features.UploadMyProfilePhoto;

internal sealed record UploadMyProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string ScanStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
