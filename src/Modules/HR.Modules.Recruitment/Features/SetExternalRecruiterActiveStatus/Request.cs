namespace HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;

internal sealed record SetExternalRecruiterActiveStatusRequest(
    Guid CompanyId,
    Guid ExternalRecruiterId,
    bool IsActive);
