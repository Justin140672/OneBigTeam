namespace HR.Modules.Recruitment.Features.ReorderRecruitmentStages;

// OrderedStageIds is the full ordered list of the company's stage ids — DisplayOrder is reassigned
// 1..N based on list position (ticket #97).
internal sealed record ReorderRecruitmentStagesRequest(
    Guid CompanyId,
    IReadOnlyList<Guid> OrderedStageIds);
