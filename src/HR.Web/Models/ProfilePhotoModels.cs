namespace HR.Web.Models;

// ── Self-service ("my profile photo") ──────────────────────────────────────

public sealed record GetMyProfilePhotoResponse(
    CurrentProfilePhotoDto? CurrentPhoto,
    PendingProfilePhotoDto? PendingPhoto);

public sealed record CurrentProfilePhotoDto(
    Guid Id,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset UpdatedAt);

public sealed record PendingProfilePhotoDto(
    Guid Id,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt);

public sealed record UploadMyProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── HR-facing (direct upload, pending review) ───────────────────────────────

public sealed record UploadEmployeeProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GetPendingProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GetPendingProfilePhotoByIdResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GetEmployeeProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ApproveProfilePhotoResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string FileName,
    long FileSize,
    string ContentType,
    string DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RejectProfilePhotoResponse(
    Guid PendingProfilePhotoId,
    Guid EmployeeId,
    string? RejectionReason,
    Guid ReviewedBy,
    DateTimeOffset ReviewedAt);
