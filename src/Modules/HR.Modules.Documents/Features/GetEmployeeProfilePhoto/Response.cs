namespace HR.Modules.Documents.Features.GetEmployeeProfilePhoto;

internal sealed record GetEmployeeProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
