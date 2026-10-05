namespace HR.Modules.Recruitment.Features.AddInterviewStage;

internal sealed record AddInterviewStageRequest(Guid CompanyId, string? Name = null);
