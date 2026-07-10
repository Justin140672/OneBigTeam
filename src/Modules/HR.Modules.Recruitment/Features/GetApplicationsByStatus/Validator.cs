using FluentValidation;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

internal sealed class GetApplicationsByStatusValidator : AbstractValidator<GetApplicationsByStatusRequest>
{
    private static readonly ApplicationStatus[] FunnelStages =
    [
        ApplicationStatus.Applied,
        ApplicationStatus.Screening,
        ApplicationStatus.InterviewScheduled,
        ApplicationStatus.Interviewed,
        ApplicationStatus.Offered,
        ApplicationStatus.Hired,
    ];

    public GetApplicationsByStatusValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.Status)
            .Must(status => FunnelStages.Contains(status))
            .WithMessage("Status must be one of the active pipeline stages (Applied, Screening, InterviewScheduled, Interviewed, Offered, Hired).");
    }
}
