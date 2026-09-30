namespace HR.Modules.Recruitment.Features.GetInternalVacancy;

internal sealed record GetInternalVacancyResponse(
    Guid Id,
    string Title,
    string? Description,
    string? DepartmentName,
    string? Location,
    string? EmploymentType,
    DateOnly? ClosingDate,
    DateOnly? OpenedAt,
    decimal? SalaryMin = null,
    decimal? SalaryMax = null,
    string? SalaryType = null);
