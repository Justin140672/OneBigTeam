namespace HR.Modules.Employees.Features.GetEmployeePromotionHistory;

internal sealed record EmployeePromotionHistoryItem(
    Guid Id,
    string PreviousPositionProfileTitle,
    string NewPositionProfileTitle,
    DateOnly EffectiveDate,
    string Reason,
    string? Notes,
    string CreatedByName,
    DateTimeOffset CreatedDate,
    DateTimeOffset? CompletedAt);

internal sealed record GetEmployeePromotionHistoryResponse(IReadOnlyList<EmployeePromotionHistoryItem> Items);
