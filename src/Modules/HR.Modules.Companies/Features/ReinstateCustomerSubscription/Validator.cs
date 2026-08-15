using FluentValidation;

namespace HR.Modules.Companies.Features.ReinstateCustomerSubscription;

internal sealed class ReinstateCustomerSubscriptionValidator : AbstractValidator<ReinstateCustomerSubscriptionRequest>
{
    public ReinstateCustomerSubscriptionValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
