namespace HR.Modules.Recruitment.Features.GetInternalVacancy;

internal sealed record GetInternalVacancyResponse(
    Guid Id,
    string Title,
    string? Description,
    string? DepartmentName,
    string? Location,
    // Not available on Vacancy yet — always null, kept for forward-compat.
    string? EmploymentType,
    // No closing-date field exists on Vacancy yet — always null, kept for forward-compat.
    DateOnly? ClosingDate,
    DateOnly? OpenedAt);
