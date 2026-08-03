using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.PublishVacancy;

internal sealed record PublishVacancyResponse(
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
