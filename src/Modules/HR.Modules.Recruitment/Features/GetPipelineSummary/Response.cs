namespace HR.Modules.Recruitment.Features.GetPipelineSummary;

internal sealed record GetPipelineSummaryResponse(IReadOnlyList<PipelineSummaryItem> Items);

internal sealed record PipelineSummaryItem(
    Guid StageId,
    string Status,
    int ApplicationCount);
