namespace HR.Modules.Documents.Features.GetMyProfilePhoto;

internal sealed record GetMyProfilePhotoResponse(
    CurrentPhotoDto? CurrentPhoto,
    PendingPhotoDto? PendingPhoto);

internal sealed record CurrentPhotoDto(
    Guid Id,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset UpdatedAt);

internal sealed record PendingPhotoDto(
    Guid Id,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt);
