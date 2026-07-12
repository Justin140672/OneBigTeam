namespace HR.Modules.Documents.Features.UploadEmployeeProfilePhoto;

internal sealed record UploadEmployeeProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
