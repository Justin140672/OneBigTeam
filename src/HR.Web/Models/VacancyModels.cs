using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListVacanciesResponse(List<VacancyListItemModel> Items);

public record VacancyListItemModel(
    Guid Id,
    Guid PositionProfileId,
    string? AdvertTitle,
    string Status,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    string? PositionProfileTitle,
    Guid? PositionProfileDepartmentId,
    string EffectiveTitle,
    // Resolved exclusively from the linked Position Profile — a vacancy no longer has a location of
    // its own to override.
    string? EffectiveLocation);

// ── DASHBOARD: STALE VACANCIES ──────────────────────────────────────────────────

public record GetStaleVacanciesResponse(IReadOnlyList<StaleVacancyItem> Items);

public record StaleVacancyItem(
    Guid VacancyId,
    string Title,
    DateOnly? OpenedAt,
    DateTimeOffset? LastActivityAt,
    int DaysSinceActivity);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    string Status,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? PositionProfileTitle,
    Guid? PositionProfileDepartmentId,
    string? PositionProfileDescription,
    bool? PositionProfileIsActive,
    string EffectiveTitle,
    string? EffectiveLocation,
    int ApplicationCount,
    bool CanChangePositionProfile);

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreateVacancyRequest(
    Guid CompanyId,
    Guid PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId = null);

public record CreateVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    string Status,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdateVacancyRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid? PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId = null,
    bool IsAuthorisedCorrection = false,
    string? CorrectionReason = null);

public record UpdateVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    string Status,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── CLOSE ─────────────────────────────────────────────────────────────────────

public record CloseVacancyRequest(Guid CompanyId, Guid VacancyId, DateOnly? ClosedAt);

public record CloseVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    string Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class VacancyEditModel
{
    public string? AdvertTitle { get; set; }
    public string? AdvertDescription { get; set; }
    [Required(ErrorMessage = "Position profile is required.")]
    public Guid? PositionProfileId { get; set; }
    [Required(ErrorMessage = "Hiring manager is required.")]
    public Guid? HiringManagerId { get; set; }

    // Authorised correction escape hatch — only meaningful when PositionProfileId is being changed on
    // a vacancy that's no longer eligible for the normal Draft+no-applications path. See
    // VacancyDetail.razor.
    public bool IsAuthorisedCorrection { get; set; }
    public string? CorrectionReason { get; set; }
}

// ── DASHBOARD: HIRING PIPELINE SUMMARY ──────────────────────────────────────────

public sealed record GetPipelineSummaryResponse(IReadOnlyList<PipelineSummaryItem> Items);

public sealed record PipelineSummaryItem(
    string Status,
    int ApplicationCount);
