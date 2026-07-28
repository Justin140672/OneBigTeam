using FluentValidation;

namespace HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;

internal sealed class SetExternalRecruiterActiveStatusValidator : AbstractValidator<SetExternalRecruiterActiveStatusRequest>
{
    public SetExternalRecruiterActiveStatusValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.ExternalRecruiterId)
            .NotEmpty();
    }
}
