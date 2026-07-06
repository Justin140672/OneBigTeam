using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed record ListVacanciesResponse(IReadOnlyList<VacancyListItem> Items);

internal sealed record VacancyListItem(
    Guid Id,
    Guid? DepartmentId,
    string Title,
    string? Location,
    VacancyStatus Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt);
