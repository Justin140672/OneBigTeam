namespace HR.Web.Models;

public sealed record CurrentCompensationModel(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SalaryType,
    decimal Salary,
    decimal? AnnualisedSalary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CompensationHistoryItemModel(
    Guid Id,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record GetCompensationHistoryResponse(IReadOnlyList<CompensationHistoryItemModel> Items);
