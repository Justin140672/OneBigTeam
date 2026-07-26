namespace HR.Web.Models;

public sealed record EmployeePromotionHistoryItemModel(
    Guid Id,
    string PreviousPositionProfileTitle,
    string NewPositionProfileTitle,
    DateOnly EffectiveDate,
    string Reason,
    string? Notes,
    string CreatedByName,
    DateTimeOffset CreatedDate,
    DateTimeOffset? CompletedAt);

public sealed record GetEmployeePromotionHistoryResponse(IReadOnlyList<EmployeePromotionHistoryItemModel> Items);

// ── PROMOTE ──────────────────────────────────────────────────────────────────

public sealed record PromoteEmployeeRequest(
    Guid CompanyId,
    Guid EmployeeId,
    Guid NewPositionProfileId,
    DateOnly EffectiveDate,
    string Reason,
    string? Notes,
    Guid? NewManagerId,
    Guid? NewLocationId,
    bool ConfirmBackdatedEffectiveDate,
    bool CreateCompensationChange,
    string? CompensationSalaryType,
    decimal? CompensationSalary,
    string? CompensationCurrency,
    decimal? CompensationHoursPerWeek,
    decimal? CompensationFte,
    string? CompensationNotes);

public sealed record PromoteEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid PreviousPositionProfileId,
    Guid NewPositionProfileId,
    Guid? NewManagerId,
    Guid? NewLocationId,
    DateOnly EffectiveDate,
    string Reason,
    string? Notes,
    Guid? CompensationId,
    DateTimeOffset CreatedDate,
    DateTimeOffset? CompletedAt);
