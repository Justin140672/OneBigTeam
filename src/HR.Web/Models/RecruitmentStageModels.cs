using System.ComponentModel.DataAnnotations;

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

// DSH-04: explicit, machine-readable stage purpose. Only valid on non-terminal stages
// (server rejects a purpose on a terminal stage). Serializes as a string. null == no metric meaning.
public enum RecruitmentStagePurpose
{
    NewApplication,
    Interview,
    Offer,
}

public sealed record ListRecruitmentStagesResponse(IReadOnlyList<RecruitmentStageListItem> Items);

public sealed record RecruitmentStageListItem(
    Guid Id,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    RecruitmentStagePurpose? Purpose = null);

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
    RecruitmentStagePurpose? Purpose = null);

public sealed record UpdateRecruitmentStageResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    DateTimeOffset UpdatedAt);

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

public sealed class RecruitmentStageEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    public RecruitmentStageTerminalOutcome TerminalOutcome { get; set; } = RecruitmentStageTerminalOutcome.None;

    // DSH-04: nullable — "None" in the picker maps to null. Server rejects a non-null purpose on a
    // terminal stage, so the edit screen hides/disables the picker when IsTerminal is true.
    public RecruitmentStagePurpose? Purpose { get; set; }

    public bool IsTerminal => TerminalOutcome != RecruitmentStageTerminalOutcome.None;
}
