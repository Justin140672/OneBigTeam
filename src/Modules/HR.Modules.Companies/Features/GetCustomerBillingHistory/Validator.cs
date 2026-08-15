using FluentValidation;

namespace HR.Modules.Companies.Features.GetCustomerBillingHistory;

internal sealed class GetCustomerBillingHistoryValidator : AbstractValidator<GetCustomerBillingHistoryRequest>
{
    public GetCustomerBillingHistoryValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
