using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListOnboardingTemplatesResponse(IReadOnlyList<OnboardingTemplateListItemModel> Items);

public record OnboardingTemplateListItemModel(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int TaskCount);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetOnboardingTemplateResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OnboardingTemplateTaskModel> Tasks);

public record OnboardingTemplateTaskModel(
    Guid Id,
    string Title,
    string? Description,
    string Priority,
    string AssignTo,
    int DueDaysAfterStart,
    int DisplayOrder);

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreateOnboardingTemplateRequest(Guid CompanyId, string Name, string? Description);

public record CreateOnboardingTemplateResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdateOnboardingTemplateRequest(
    Guid CompanyId,
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<UpdateOnboardingTemplateTaskItem> Tasks);

public record UpdateOnboardingTemplateTaskItem(
    Guid? Id,
    string Title,
    string? Description,
    string Priority,
    string AssignTo,
    int DueDaysAfterStart,
    int DisplayOrder);

public record UpdateOnboardingTemplateResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OnboardingTemplateTaskModel> Tasks);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class OnboardingTemplateEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<OnboardingTemplateTaskEditModel> Tasks { get; set; } = [];
}

public sealed class OnboardingTemplateTaskEditModel
{
    public Guid? Id { get; set; }
    [Required(ErrorMessage = "Task title is required.")]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = "Medium";
    public string AssignTo { get; set; } = "Unassigned";
    [Range(0, int.MaxValue, ErrorMessage = "Due days after start cannot be negative.")]
    public int DueDaysAfterStart { get; set; }
    public int DisplayOrder { get; set; }
}
