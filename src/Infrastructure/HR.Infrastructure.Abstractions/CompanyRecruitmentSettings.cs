namespace HR.Infrastructure.Abstractions;

public sealed record CompanyRecruitmentSettings(
    bool VacancyApprovalRequired,
    bool OfferApprovalRequired,
    int CandidateRetentionDays)
{
    public static readonly CompanyRecruitmentSettings Default = new(
        VacancyApprovalRequired: false,
        OfferApprovalRequired: false,
        CandidateRetentionDays: 730);
}
