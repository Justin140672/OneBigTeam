using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

internal sealed record GetApplicationsByStatusRequest(
    Guid CompanyId,
    ApplicationStatus Status);
