namespace HR.Modules.Recruitment.Features.ListInternalVacancies;

internal sealed record ListInternalVacanciesResponse(IReadOnlyList<InternalVacancyListItem> Items);

internal sealed record InternalVacancyListItem(
    Guid Id,
    string Title,
    string? DepartmentName,
    string? Location,
    // No closing-date field exists on Vacancy yet — always null, kept on the DTO for forward-compat.
    DateOnly? ClosingDate);
