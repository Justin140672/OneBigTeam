namespace HR.Modules.Recruitment.Features.ReorderRecruitmentStages;

internal sealed record ReorderRecruitmentStagesResponse(IReadOnlyList<ReorderedStageItem> Items);

internal sealed record ReorderedStageItem(Guid Id, string Name, int DisplayOrder);
