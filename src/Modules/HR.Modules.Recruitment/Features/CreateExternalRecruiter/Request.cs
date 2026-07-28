namespace HR.Modules.Recruitment.Features.CreateExternalRecruiter;

internal sealed record CreateExternalRecruiterRequest(
    Guid CompanyId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes);
