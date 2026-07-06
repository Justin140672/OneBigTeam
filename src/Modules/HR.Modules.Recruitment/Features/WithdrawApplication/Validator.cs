using FluentValidation;

namespace HR.Modules.Recruitment.Features.WithdrawApplication;

internal sealed class WithdrawApplicationValidator : AbstractValidator<WithdrawApplicationRequest>
{
    public WithdrawApplicationValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();
    }
}
