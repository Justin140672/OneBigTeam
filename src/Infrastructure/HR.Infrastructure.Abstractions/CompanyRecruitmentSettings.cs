namespace HR.Infrastructure.Abstractions;

/// <summary>
/// SET-05: narrow, read-only projection of the recruitment-related fields on Companies'
/// CompanySettings, exposed to HR.Modules.Recruitment via <see cref="ICompanyRecruitmentSettingsReader"/>
/// so Recruitment can enforce company-configured approval/retention rules without referencing
/// HR.Modules.Companies directly (mirrors ICompanySicknessSettingsReader/ICompanyProbationSettingsReader).
/// </summary>
public sealed record CompanyRecruitmentSettings(
    bool VacancyApprovalRequired,
    bool OfferApprovalRequired,
    int CandidateRetentionDays)
{
    /// <summary>Backward-compatible defaults for a company with no persisted CompanySettings row yet
    /// (mirrors CompanySicknessSettings.Default) — approvals off, 2-year (730 day) retention.</summary>
    public static readonly CompanyRecruitmentSettings Default = new(
        VacancyApprovalRequired: false,
        OfferApprovalRequired: false,
        CandidateRetentionDays: 730);
}
