namespace HR.Modules.Recruitment.Features.UpdateExternalRecruiter;

internal sealed record UpdateExternalRecruiterRequest(
    Guid CompanyId,
    Guid ExternalRecruiterId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes);
