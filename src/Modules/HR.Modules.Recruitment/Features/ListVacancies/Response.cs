using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed record ListVacanciesResponse(IReadOnlyList<VacancyListItem> Items);

internal sealed record VacancyListItem(
    Guid Id,
    Guid PositionProfileId,
    string? AdvertTitle,
    VacancyStatus Status,
    Guid HiringManagerId,
    Guid? AssignedRecruiterId,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    DateTimeOffset CreatedAt,
    // Canonical title/department from the linked Position Profile — see GetVacancyResponse's remarks
    // for the same additive/null-when-unresolved behaviour. Description is intentionally omitted here
    // (kept on GetVacancyResponse only) to keep the list payload light.
    string? PositionProfileTitle,
    Guid? PositionProfileDepartmentId,
    // Resolved at the read layer — see GetVacancyResponse's EffectiveTitle remarks.
    string EffectiveTitle,
    // Location comes exclusively from the linked Position Profile — see GetVacancyResponse's
    // EffectiveLocation remarks.
    string? EffectiveLocation,
    int ApplicationCount);
