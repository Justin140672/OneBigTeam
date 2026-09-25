using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CreateCandidateApplication;

internal sealed record CreateCandidateApplicationResponse(
    Guid CandidateId,
    Guid ApplicationId,
    Guid CompanyId,
    Guid VacancyId,
    string FirstName,
    string LastName,
    string Email,
    Guid CurrentStageId,
    Guid? CvDocumentId,
    ApplicationSource? Source,
    Guid? SourceExternalRecruiterId,
    DateTimeOffset AppliedAt);

/// <summary>
/// 409 body returned when a candidate with the same email already exists in the company. Carries the
/// existing record so the client can offer to select it instead of creating a duplicate. A superset of
/// the standard <c>{ error, code }</c> problem body.
/// </summary>
internal sealed record CreateCandidateApplicationDuplicateCandidateResponse(
    string Error,
    string Code,
    Guid ExistingCandidateId,
    string ExistingCandidateFirstName,
    string ExistingCandidateLastName,
    string ExistingCandidateEmail,
    bool ExistingCandidateIsActive)
{
    public const string ErrorCode = "candidate_email_exists";
}
