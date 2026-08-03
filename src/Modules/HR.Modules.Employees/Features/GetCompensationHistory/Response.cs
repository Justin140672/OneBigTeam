namespace HR.Modules.Employees.Features.GetCompensationHistory;

internal sealed record CompensationHistoryItem(
    Guid Id,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    string Reason,
    Guid CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt);

internal sealed record GetCompensationHistoryResponse(IReadOnlyList<CompensationHistoryItem> Items);
