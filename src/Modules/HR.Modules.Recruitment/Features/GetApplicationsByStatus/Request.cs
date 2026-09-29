namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

internal sealed record GetApplicationsByStatusRequest(
    Guid CompanyId,
    Guid StageId);
