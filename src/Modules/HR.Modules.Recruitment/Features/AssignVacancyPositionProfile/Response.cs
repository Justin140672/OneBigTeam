using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.AssignVacancyPositionProfile;

internal sealed record AssignVacancyPositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? PositionProfileId,
    string? AdvertTitle,
    VacancyStatus Status,
    DateTimeOffset UpdatedAt);
