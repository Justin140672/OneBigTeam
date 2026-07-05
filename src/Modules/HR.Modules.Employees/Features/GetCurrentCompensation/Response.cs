namespace HR.Modules.Employees.Features.GetCurrentCompensation;

internal sealed record GetCurrentCompensationResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
