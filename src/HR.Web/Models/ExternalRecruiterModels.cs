using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;


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
    int LinkedVacancyCount,
    DateTimeOffset CreatedAt);


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
    DateTimeOffset UpdatedAt,
    // Ticket 2: optimistic-concurrency token.
    int Version = 0);


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


public record UpdateExternalRecruiterRequest(
    Guid CompanyId,
    Guid ExternalRecruiterId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

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
    DateTimeOffset UpdatedAt,
    int Version = 0);


public record SetExternalRecruiterActiveStatusRequest(Guid CompanyId, Guid ExternalRecruiterId, bool IsActive);

public record SetExternalRecruiterActiveStatusResponse(
    Guid Id,
    Guid CompanyId,
    string AgencyName,
    bool IsActive,
    DateTimeOffset UpdatedAt);


public record GetExternalRecruiterActivitySummaryResponse(
    Guid ExternalRecruiterId,
    string AgencyName,
    IReadOnlyList<VacancyActivityItemModel> CurrentVacancies,
    IReadOnlyList<VacancyActivityItemModel> PreviousVacancies,
    int CandidatesIntroducedCount,
    int CandidatesHiredCount);

public record VacancyActivityItemModel(
    Guid VacancyId,
    string? AdvertTitle,
    string Status,
    DateOnly? DateInstructed);


public sealed record GetExternalRecruiterUsageResponse(
    Guid ExternalRecruiterId,
    bool InUse,
    int ActiveVacancyCount,
    IReadOnlyList<string> VacancyLabels);


public sealed class ExternalRecruiterEditModel : IHasVersion
{
    public int Version { get; set; }

    [Required(ErrorMessage = "Agency name is required.")]
    public string AgencyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? ContactEmail { get; set; }
    public string? ContactTelephone { get; set; }
    public string? Website { get; set; }
    public string? Notes { get; set; }
}
