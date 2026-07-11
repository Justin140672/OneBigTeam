namespace HR.Web.Models;

public sealed record OffboardingOverviewModel(
    Guid EmployeeId,
    bool HasPlan,
    string? PlanStatus,
    DateOnly? LastWorkingDay,
    string? Notes,
    IReadOnlyList<OffboardingTaskOverviewItem> Tasks);

public sealed record OffboardingTaskOverviewItem(
    Guid Id,
    string Title,
    string? Description,
    string AssignTo,
    string Status,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StartOffboardingResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    string Status,
    string? Notes,
    IReadOnlyList<Guid> GeneratedTaskIds,
    DateTimeOffset CreatedAt);
