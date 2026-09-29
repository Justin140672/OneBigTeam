using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ApplyForInternalVacancy;

/// <summary>
/// Returned to the applying employee. Deliberately excludes recruiter-facing data (pipeline stage,
/// notes, reviewers).
/// </summary>
internal sealed record ApplyForInternalVacancyResponse(
    Guid ApplicationId,
    Guid CompanyId,
    Guid VacancyId,
    Guid CandidateId,
    Guid CvDocumentId,
    ApplicationSource Source,
    DateTimeOffset AppliedAt);

/// <summary>
/// A refusal that carries its own machine-readable <see cref="Code"/> so the employee UI (Ticket 5)
/// can show a specific message. Body shape matches the standard <c>{ error, code }</c> problem body.
/// </summary>
internal sealed record ApplyForInternalVacancyRejection(int StatusCode, string Code, string Error)
{
    public const string AlreadyAppliedCode = "already_applied";

    public const string NotEligibleCode = "not_eligible_to_apply";

    public const string EmailInUseCode = "applicant_email_in_use";
}
