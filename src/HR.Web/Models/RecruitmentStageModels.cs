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

public sealed record ListRecruitmentStagesResponse(IReadOnlyList<RecruitmentStageListItem> Items);

public sealed record RecruitmentStageListItem(
    Guid Id,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome);

public sealed record CreateRecruitmentStageRequest(
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome);

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
    RecruitmentStageTerminalOutcome TerminalOutcome);

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

public sealed class RecruitmentStageEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    public RecruitmentStageTerminalOutcome TerminalOutcome { get; set; } = RecruitmentStageTerminalOutcome.None;

    public bool IsTerminal => TerminalOutcome != RecruitmentStageTerminalOutcome.None;
}
