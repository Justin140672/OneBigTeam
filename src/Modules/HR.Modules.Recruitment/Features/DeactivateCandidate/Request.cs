namespace HR.Modules.Recruitment.Features.DeactivateCandidate;

internal sealed record DeactivateCandidateRequest(
    Guid CompanyId,
    Guid CandidateId,
    string Reason);
