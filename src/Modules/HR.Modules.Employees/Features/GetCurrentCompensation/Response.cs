namespace HR.Modules.Employees.Features.GetCurrentCompensation;

internal sealed record GetCurrentCompensationResponse(
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
    string Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
