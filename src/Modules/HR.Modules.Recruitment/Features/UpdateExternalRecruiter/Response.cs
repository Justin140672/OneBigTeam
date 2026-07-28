namespace HR.Modules.Recruitment.Features.UpdateExternalRecruiter;

internal sealed record UpdateExternalRecruiterResponse(
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
