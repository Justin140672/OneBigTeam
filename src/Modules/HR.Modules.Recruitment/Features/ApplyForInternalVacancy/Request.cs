using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ApplyForInternalVacancy;

/// <summary>
/// Internal recruitment Ticket 4: multipart/form-data request by which the signed-in employee applies
/// for an internally advertised vacancy. Deliberately carries no applicant identity (no employee id,
/// name, email or source) — the applicant is always the authenticated employee, and their details are
/// taken from their Employee record server-side. A CV file is required.
/// </summary>
internal sealed class ApplyForInternalVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }

    public IFormFile? CvFile { get; init; }
}
