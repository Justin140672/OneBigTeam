namespace HR.Modules.Recruitment.Features.ReactivateCandidate;

internal sealed record ReactivateCandidateRequest(
    Guid CompanyId,
    Guid CandidateId);
