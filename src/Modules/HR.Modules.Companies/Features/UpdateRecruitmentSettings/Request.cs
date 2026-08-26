namespace HR.Modules.Companies.Features.UpdateRecruitmentSettings;

/// <summary>SET-05: recruitment approval/retention settings, kept as their own slice separate from
/// UpdateHrSettings (mirrors how asset numbering is split out via UpdateAssetNumberSettings).</summary>
internal sealed record UpdateRecruitmentSettingsRequest
{
    public Guid CompanyId { get; init; }
    public bool VacancyApprovalRequired { get; init; }
    public bool OfferApprovalRequired { get; init; }
    public int CandidateRetentionDays { get; init; } = 730;

    /// <summary>See UpdateCompanySettingsRequest.Version (SET-03) — same optimistic-concurrency scheme.</summary>
    public int Version { get; init; }
}
