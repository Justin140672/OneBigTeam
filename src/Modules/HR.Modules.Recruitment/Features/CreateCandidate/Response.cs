namespace HR.Modules.Recruitment.Features.CreateCandidate;

internal sealed record CreateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
