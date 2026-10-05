using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetVacancy;

internal sealed record GetVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid PositionProfileId,
    string? AdvertTitle,
    string? AdvertDescription,
    VacancyStatus Status,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId,
    bool IsAdvertisedInternally,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? PositionProfileTitle,
    Guid? PositionProfileDepartmentId,
    bool? PositionProfileIsActive,
    string EffectiveTitle,
    string? EffectiveLocation,
    int ApplicationCount,
    bool CanChangePositionProfile,
    int Version,
    Guid? EmploymentTypeId = null);
