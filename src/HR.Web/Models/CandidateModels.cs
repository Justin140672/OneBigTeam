using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;


public record ListCandidatesResponse(
    List<CandidateListItemModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

public record CandidateListItemModel(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    DateTimeOffset CreatedAt,
    bool IsActive = true)
{
    public string FullName => $"{FirstName} {LastName}";
}


public record GetCandidateResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    Guid? EmployeeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsActive = true,
    DateTimeOffset? DeactivatedAt = null,
    Guid? DeactivatedByUserId = null,
    string? DeactivationReason = null,
    // Ticket 2: optimistic-concurrency token.
    int Version = 0);


public record DeactivateCandidateRequest(
    Guid CompanyId,
    Guid CandidateId,
    string Reason);

public record DeactivateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    bool IsActive,
    DateTimeOffset? DeactivatedAt,
    Guid? DeactivatedByUserId,
    string? DeactivationReason,
    DateTimeOffset UpdatedAt);

public record ReactivateCandidateRequest(
    Guid CompanyId,
    Guid CandidateId);

public record ReactivateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    bool IsActive,
    DateTimeOffset? ReactivatedAt,
    Guid? ReactivatedByUserId,
    DateTimeOffset UpdatedAt);


public record CreateCandidateRequest(
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl);

public record CreateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);


public record UpdateCandidateRequest(
    Guid CompanyId,
    Guid CandidateId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version = 0);


public record ListCandidateDocumentsResponse(List<CandidateDocumentListItemModel> Items);

public record CandidateDocumentListItemModel(
    Guid Id,
    string Title,
    string Kind,
    string FileName,
    long FileSize,
    string ContentType,
    DateTimeOffset CreatedAt,
    bool IsCurrentCv = false,
    int ReferencingApplicationCount = 0,
    string? ScanStatus = null,
    bool IsDownloadable = false);

public record UploadedCandidateDocumentModel(
    Guid Id,
    Guid CompanyId,
    Guid CandidateId,
    string Title,
    string Kind,
    string FileName,
    long FileSize,
    string ContentType,
    DateTimeOffset CreatedAt,
    string? ScanStatus = null);

public static class CandidateDocumentScanStatuses
{
    public const string Pending = "Pending";
    public const string Scanning = "Scanning";
    public const string Clean = "Clean";
    public const string Infected = "Infected";
    public const string Failed = "Failed";

    public static bool IsClean(string? status) =>
        string.Equals(status, Clean, StringComparison.OrdinalIgnoreCase);

    public static bool IsInProgress(string? status) =>
        string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Scanning, StringComparison.OrdinalIgnoreCase);

    public static bool IsInfected(string? status) =>
        string.Equals(status, Infected, StringComparison.OrdinalIgnoreCase);
}


public sealed class CandidateEditModel : IHasVersion
{
    public int Version { get; set; }

    [Required(ErrorMessage = "First name is required.")]
    public string FirstName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Last name is required.")]
    public string LastName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ResumeUrl { get; set; }
}
