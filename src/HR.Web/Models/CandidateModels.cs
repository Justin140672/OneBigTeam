using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

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

// ── GET ───────────────────────────────────────────────────────────────────────

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

// ── DEACTIVATE / REACTIVATE ─────────────────────────────────────────────────

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

// ── CREATE ────────────────────────────────────────────────────────────────────

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

// ── UPDATE ────────────────────────────────────────────────────────────────────

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

// ── DOCUMENTS (Ticket #1) ─────────────────────────────────────────────────────

public record ListCandidateDocumentsResponse(List<CandidateDocumentListItemModel> Items);

public record CandidateDocumentListItemModel(
    Guid Id,
    string Title,
    string? FileName,
    string? ContentType,
    long? FileSize,
    // Ticket #1: "Cv" or "Other".
    string Kind,
    DateTimeOffset UploadedAt);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

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
