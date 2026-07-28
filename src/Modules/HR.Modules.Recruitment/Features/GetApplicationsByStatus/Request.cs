namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

// Ticket #99: filters by a RecruitmentStage id rather than a fixed ApplicationStatus value — kept
// under the existing feature/folder name to minimise churn on this well-established endpoint route.
internal sealed record GetApplicationsByStatusRequest(
    Guid CompanyId,
    Guid StageId);
