using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListVacanciesResponse(List<VacancyListItemModel> Items);

public record VacancyListItemModel(
    Guid Id,
    Guid? DepartmentId,
    string Title,
    string? Location,
    string Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt);

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
    Guid? DepartmentId,
    string Title,
    string? Description,
    string? Location,
    string Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreateVacancyRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    string? Location,
    Guid HiringManagerId);

public record CreateVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    string? Location,
    string Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdateVacancyRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    string? Location,
    Guid HiringManagerId);

public record UpdateVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    string? Location,
    string Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── CLOSE ─────────────────────────────────────────────────────────────────────

public record CloseVacancyRequest(Guid CompanyId, Guid VacancyId, DateOnly? ClosedAt);

public record CloseVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    string? Location,
    string Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class VacancyEditModel
{
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public Guid? DepartmentId { get; set; }
    [Required(ErrorMessage = "Hiring manager is required.")]
    public Guid? HiringManagerId { get; set; }
}

// ── DASHBOARD: HIRING PIPELINE SUMMARY ──────────────────────────────────────────

public sealed record GetPipelineSummaryResponse(IReadOnlyList<PipelineSummaryItem> Items);

public sealed record PipelineSummaryItem(
    string Status,
    int ApplicationCount);
