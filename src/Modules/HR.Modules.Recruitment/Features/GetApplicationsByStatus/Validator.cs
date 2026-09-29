using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

internal sealed class GetApplicationsByStatusValidator : AbstractValidator<GetApplicationsByStatusRequest>
{
    public GetApplicationsByStatusValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.StageId).NotEmpty();
    }
}
