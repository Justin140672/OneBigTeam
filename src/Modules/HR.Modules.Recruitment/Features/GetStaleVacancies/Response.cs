namespace HR.Modules.Recruitment.Features.GetStaleVacancies;

internal sealed record GetStaleVacanciesResponse(IReadOnlyList<StaleVacancyItem> Items);

internal sealed record StaleVacancyItem(
    Guid VacancyId,
    string Title,
    DateOnly? OpenedAt,
    DateTimeOffset? LastActivityAt,
    int DaysSinceActivity);
