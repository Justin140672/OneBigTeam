using HR.Modules.Recruitment.Domain;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateCandidateApplication;

/// <summary>
/// Internal recruitment Ticket 3: multipart/form-data request that creates a brand-new candidate and
/// their application to the vacancy in one call. Candidate fields mirror CreateCandidate; application
/// fields mirror CreateApplication. <see cref="CvFile"/> is optional.
/// </summary>
internal sealed class CreateCandidateApplicationRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }

    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }

    public string? Notes { get; init; }
    public ApplicationSource? Source { get; init; }
    public Guid? SourceExternalRecruiterId { get; init; }

    public IFormFile? CvFile { get; init; }
}
