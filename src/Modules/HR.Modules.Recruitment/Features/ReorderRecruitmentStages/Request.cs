namespace HR.Modules.Recruitment.Features.ReorderRecruitmentStages;

// OrderedStageIds is the full ordered list of the company's stage ids — DisplayOrder is reassigned
// 1..N based on list position (ticket #97).
internal sealed record ReorderRecruitmentStagesRequest(
    Guid CompanyId,
    IReadOnlyList<Guid> OrderedStageIds)
{
    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
