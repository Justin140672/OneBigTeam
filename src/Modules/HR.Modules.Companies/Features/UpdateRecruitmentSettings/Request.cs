namespace HR.Modules.Companies.Features.UpdateRecruitmentSettings;

internal sealed record UpdateRecruitmentSettingsRequest
{
    public Guid CompanyId { get; init; }
    public bool VacancyApprovalRequired { get; init; }
    public bool OfferApprovalRequired { get; init; }
    public int CandidateRetentionDays { get; init; } = 730;

    public int Version { get; init; }
}
