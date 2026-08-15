using FluentValidation;

namespace HR.Modules.Companies.Features.CancelCustomerDeletion;

internal sealed class CancelCustomerDeletionValidator : AbstractValidator<CancelCustomerDeletionRequest>
{
    public CancelCustomerDeletionValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
