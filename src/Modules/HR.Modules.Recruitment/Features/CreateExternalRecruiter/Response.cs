namespace HR.Modules.Recruitment.Features.CreateExternalRecruiter;

internal sealed record CreateExternalRecruiterResponse(
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
