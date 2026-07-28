using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListExternalRecruitersResponse(
    IReadOnlyList<ExternalRecruiterListItemModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

public record ExternalRecruiterListItemModel(
    Guid Id,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    bool IsActive,
    // Ticket #81: count of Vacancy rows currently assigned to this recruiter (Vacancy.AssignedRecruiterId)
    // — matches ListExternalRecruitersHandler's rationale on the API side. This is now a current-snapshot
    // count, not an all-time count (the previous VacancyRecruiterAssignment history table is removed).
    int LinkedVacancyCount,
    DateTimeOffset CreatedAt);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetExternalRecruiterResponse(
    Guid Id,
    Guid CompanyId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreateExternalRecruiterRequest(
    Guid CompanyId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes);

public record CreateExternalRecruiterResponse(
    Guid Id,
    Guid CompanyId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdateExternalRecruiterRequest(
    Guid CompanyId,
    Guid ExternalRecruiterId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes);

public record UpdateExternalRecruiterResponse(
    Guid Id,
    Guid CompanyId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── ACTIVE STATUS ─────────────────────────────────────────────────────────────

public record SetExternalRecruiterActiveStatusRequest(Guid CompanyId, Guid ExternalRecruiterId, bool IsActive);

public record SetExternalRecruiterActiveStatusResponse(
    Guid Id,
    Guid CompanyId,
    string AgencyName,
    bool IsActive,
    DateTimeOffset UpdatedAt);

// ── ACTIVITY SUMMARY (#79) ────────────────────────────────────────────────────
// No fee/commission/contract/invoicing fields anywhere — explicitly out of scope.

public record GetExternalRecruiterActivitySummaryResponse(
    Guid ExternalRecruiterId,
    string AgencyName,
    IReadOnlyList<VacancyActivityItemModel> CurrentVacancies,
    IReadOnlyList<VacancyActivityItemModel> PreviousVacancies,
    int CandidatesIntroducedCount,
    int CandidatesHiredCount);

// Ticket #81: DateInstructed (from the now-removed VacancyRecruiterAssignment row) no longer exists —
// Vacancy.OpenedAt is used instead as the closest available date signal, and is nullable because a
// vacancy that never opened (still Draft) has no OpenedAt.
public record VacancyActivityItemModel(
    Guid VacancyId,
    string? AdvertTitle,
    string Status,
    DateOnly? DateInstructed);

// ── USAGE CHECK (deactivation warning) ───────────────────────────────────────

public sealed record GetExternalRecruiterUsageResponse(
    Guid ExternalRecruiterId,
    bool InUse,
    int ActiveVacancyCount,
    IReadOnlyList<string> VacancyLabels);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class ExternalRecruiterEditModel
{
    [Required(ErrorMessage = "Agency name is required.")]
    public string AgencyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? ContactEmail { get; set; }
    public string? ContactTelephone { get; set; }
    public string? Website { get; set; }
    public string? Notes { get; set; }
}
