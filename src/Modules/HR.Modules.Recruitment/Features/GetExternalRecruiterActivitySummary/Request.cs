namespace HR.Modules.Recruitment.Features.GetExternalRecruiterActivitySummary;

internal sealed record GetExternalRecruiterActivitySummaryRequest(
    Guid CompanyId,
    Guid ExternalRecruiterId);
