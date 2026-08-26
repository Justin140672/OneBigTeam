namespace HR.Modules.Companies.Features.UpdateRecruitmentSettings;

internal sealed record UpdateRecruitmentSettingsResponse(
    Guid CompanyId,
    bool VacancyApprovalRequired,
    bool OfferApprovalRequired,
    int CandidateRetentionDays,
    DateTimeOffset UpdatedAt,
    int Version);
