namespace HR.Modules.Recruitment.Features.ListInternalVacancies;

internal sealed record ListInternalVacanciesResponse(IReadOnlyList<InternalVacancyListItem> Items);

internal sealed record InternalVacancyListItem(
    Guid Id,
    string Title,
    string? DepartmentName,
    string? Location,
    DateOnly? ClosingDate,
    // Internal recruitment Ticket 4: whether the signed-in employee has already applied (any
    // application state), so the employee UI can replace "Apply" with an "Applied" indicator.
    bool HasApplied = false,
    decimal? SalaryMin = null,
    decimal? SalaryMax = null,
    string? SalaryType = null);
