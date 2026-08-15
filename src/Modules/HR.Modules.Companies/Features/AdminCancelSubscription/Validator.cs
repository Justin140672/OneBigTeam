using FluentValidation;

namespace HR.Modules.Companies.Features.AdminCancelSubscription;

internal sealed class AdminCancelSubscriptionValidator : AbstractValidator<AdminCancelSubscriptionRequest>
{
    public AdminCancelSubscriptionValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
