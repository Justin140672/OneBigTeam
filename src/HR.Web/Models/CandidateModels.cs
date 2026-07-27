using System.ComponentModel.DataAnnotations;

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
    DateTimeOffset CreatedAt)
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
    string? ResumeUrl);

public record UpdateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class CandidateEditModel
{
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
