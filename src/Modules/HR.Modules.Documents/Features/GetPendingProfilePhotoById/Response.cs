namespace HR.Modules.Documents.Features.GetPendingProfilePhotoById;

internal sealed record GetPendingProfilePhotoByIdResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
