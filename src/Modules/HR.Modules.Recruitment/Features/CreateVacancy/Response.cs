using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed record CreateVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    VacancyStatus Status,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
