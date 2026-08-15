using FluentValidation;

namespace HR.Modules.Companies.Features.GetCustomerBillingBreakdown;

internal sealed class GetCustomerBillingBreakdownValidator : AbstractValidator<GetCustomerBillingBreakdownRequest>
{
    public GetCustomerBillingBreakdownValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
