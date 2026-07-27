using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetVacancy;

internal sealed record GetVacancyResponse(
    Guid Id,
    Guid CompanyId,
    Guid PositionProfileId,
    // Optional recruitment-specific overrides — see Vacancy.AdvertTitle/AdvertDescription's remarks.
    // May be null; use EffectiveTitle/PositionProfileTitle and PositionProfileDescription for display
    // purposes that always need a value.
    string? AdvertTitle,
    string? AdvertDescription,
    VacancyStatus Status,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Canonical role information from the linked Position Profile (HR.Modules.Employees), resolved via
    // IPositionProfileReader. Null only if the linked profile can no longer be found (PositionProfileId
    // itself is now always populated — see Vacancy.PositionProfileId).
    string? PositionProfileTitle,
    Guid? PositionProfileDepartmentId,
    string? PositionProfileDescription,
    bool? PositionProfileIsActive,
    // Resolved at the read layer: AdvertTitle ?? PositionProfileTitle ?? "(untitled)" — always
    // populated so callers that just need something to display never have to null-check AdvertTitle.
    string EffectiveTitle,
    // Location comes exclusively from the linked Position Profile — a vacancy no longer has a
    // location of its own to override. Null only if the linked profile can no longer be found or has
    // no location set.
    string? EffectiveLocation,
    // Number of Application rows currently linked to this vacancy. Exposed so the UI can explain why
    // the Position Profile field is locked (see CanChangePositionProfile) without a separate call.
    int ApplicationCount,
    // Baseline "safely changeable" rule from UpdateVacancyHandler.CanChangePositionProfile: true only
    // while the vacancy is Draft and has zero applications. The "Prevent Invalid Position Profile
    // Changes" story is expected to layer an authorised override on top of this for the UI as well.
    bool CanChangePositionProfile);
