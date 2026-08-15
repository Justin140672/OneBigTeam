using FluentValidation;

namespace HR.Modules.Companies.Features.ResumeCustomerService;

internal sealed class ResumeCustomerServiceValidator : AbstractValidator<ResumeCustomerServiceRequest>
{
    public ResumeCustomerServiceValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
