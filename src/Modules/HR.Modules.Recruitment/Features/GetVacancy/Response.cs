using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetVacancy;

internal sealed record GetVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    string? Location,
    VacancyStatus Status,
    Guid HiringManagerId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
