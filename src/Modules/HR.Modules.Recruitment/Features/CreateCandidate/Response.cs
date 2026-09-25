namespace HR.Modules.Recruitment.Features.CreateCandidate;

internal sealed record CreateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 409 body returned when a candidate with the same (case/whitespace-insensitive) email already
/// exists in the company. Same shape and <see cref="ErrorCode"/> as the combined intake's duplicate
/// response (CreateCandidateApplicationDuplicateCandidateResponse) so clients handle both identically;
/// a superset of the previous <c>{ error }</c> body.
/// </summary>
internal sealed record CreateCandidateDuplicateCandidateResponse(
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
