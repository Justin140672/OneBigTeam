using FluentValidation;

namespace HR.Modules.Companies.Features.ForceCustomerReadOnly;

internal sealed class ForceCustomerReadOnlyValidator : AbstractValidator<ForceCustomerReadOnlyRequest>
{
    public ForceCustomerReadOnlyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
