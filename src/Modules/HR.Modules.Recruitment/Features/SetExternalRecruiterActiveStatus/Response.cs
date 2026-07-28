namespace HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;

internal sealed record SetExternalRecruiterActiveStatusResponse(
    Guid Id,
    Guid CompanyId,
    string AgencyName,
    bool IsActive,
    DateTimeOffset UpdatedAt);
