namespace HR.Modules.Recruitment.Features.GetExternalRecruiter;

internal sealed record GetExternalRecruiterRequest(
    Guid CompanyId,
    Guid ExternalRecruiterId);
