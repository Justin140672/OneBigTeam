namespace HR.Modules.Recruitment.Features.GetExternalRecruiterUsage;

internal sealed record GetExternalRecruiterUsageRequest(
    Guid CompanyId,
    Guid ExternalRecruiterId);
