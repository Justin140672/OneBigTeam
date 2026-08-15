using FluentValidation;

namespace HR.Modules.Companies.Features.ExecuteCustomerDeletion;

internal sealed class ExecuteCustomerDeletionValidator : AbstractValidator<ExecuteCustomerDeletionRequest>
{
    public ExecuteCustomerDeletionValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
