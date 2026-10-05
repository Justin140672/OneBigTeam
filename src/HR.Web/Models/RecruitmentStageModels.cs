using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;

// Client-side mirror of HR.Modules.Recruitment.Domain.RecruitmentStageTerminalOutcome. Must not
// reference the module's internal enum type directly — HR.Web must not reference modules.
// Serializes as a string (see HrApiJsonOptions.Default's JsonStringEnumConverter), matching the
// server's JSON options convention used throughout this codebase.
public enum RecruitmentStageTerminalOutcome
{
    None,
    Hired,
    Rejected,
}

public enum RecruitmentStagePurpose
{
    NewApplication,
    Interview,
    Offer,
    CvReview,
}

public sealed record ListRecruitmentStagesResponse(IReadOnlyList<RecruitmentStageListItem> Items);

public sealed record RecruitmentStageListItem(
    Guid Id,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    RecruitmentStagePurpose? Purpose = null,
    // Ticket 2: optimistic-concurrency token.
    int Version = 0);

public sealed record CreateRecruitmentStageRequest(
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    RecruitmentStagePurpose? Purpose = null);

public sealed record CreateRecruitmentStageResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpdateRecruitmentStageRequest(
    Guid CompanyId,
    Guid RecruitmentStageId,
    string Name,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    RecruitmentStagePurpose? Purpose = null,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public sealed record UpdateRecruitmentStageResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    DateTimeOffset UpdatedAt,
    int Version = 0);

public sealed record ReorderRecruitmentStagesRequest(Guid CompanyId, IReadOnlyList<Guid> OrderedStageIds);

public sealed record ReorderRecruitmentStagesResponse(IReadOnlyList<ReorderedStageItem> Items);

public sealed record ReorderedStageItem(Guid Id, string Name, int DisplayOrder);

public sealed record SetRecruitmentStageActiveStatusRequest(Guid CompanyId, Guid RecruitmentStageId, bool IsActive);

public sealed record SetRecruitmentStageActiveStatusResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    DateTimeOffset UpdatedAt);

public sealed record GetRecruitmentStageUsageResponse(
    Guid RecruitmentStageId,
    bool InUse,
    int ActiveVacancyCount,
    IReadOnlyList<string> VacancyLabels);

public sealed record InterviewStageSuggestionResponse(
    string SuggestedName,
    int DisplayOrder,
    int ActiveInterviewStageCount,
    string? StageToRenameName,
    string? RenamedStageName);

public sealed record AddInterviewStageRequest(Guid CompanyId, string? Name = null);

public sealed record AddInterviewStageResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    Guid? RenamedStageId,
    string? RenamedStageName);

public sealed class InterviewStageEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(100, ErrorMessage = "Name must be 100 characters or fewer.")]
    public string Name { get; set; } = string.Empty;
}

public sealed class RecruitmentStageEditModel : IHasVersion
{
    public int Version { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    public RecruitmentStageTerminalOutcome TerminalOutcome { get; set; } = RecruitmentStageTerminalOutcome.None;

    public RecruitmentStagePurpose? Purpose { get; set; }

    public bool IsTerminal => TerminalOutcome != RecruitmentStageTerminalOutcome.None;
}
