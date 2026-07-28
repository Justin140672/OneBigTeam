namespace HR.Modules.Recruitment.Features.GetExternalRecruiter;

internal sealed record GetExternalRecruiterResponse(
    Guid Id,
    Guid CompanyId,
    string AgencyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactTelephone,
    string? Website,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
