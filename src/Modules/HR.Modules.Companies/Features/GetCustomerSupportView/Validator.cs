using FluentValidation;

namespace HR.Modules.Companies.Features.GetCustomerSupportView;

internal sealed class GetCustomerSupportViewValidator : AbstractValidator<GetCustomerSupportViewRequest>
{
    public GetCustomerSupportViewValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
