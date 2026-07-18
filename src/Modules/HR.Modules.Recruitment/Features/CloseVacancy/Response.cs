using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CloseVacancy;

internal sealed record CloseVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    VacancyStatus Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
