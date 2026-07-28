using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiter;

internal sealed class GetExternalRecruiterValidator : AbstractValidator<GetExternalRecruiterRequest>
{
    public GetExternalRecruiterValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.ExternalRecruiterId)
            .NotEmpty();
    }
}
