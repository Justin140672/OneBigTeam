using FluentValidation;

namespace HR.Modules.Companies.Features.SetCustomerOriginalStatus;

internal sealed class SetCustomerOriginalStatusValidator : AbstractValidator<SetCustomerOriginalStatusRequest>
{
    public SetCustomerOriginalStatusValidator()
    {
        RuleFor(r => r.ExpectedVersion)
            .GreaterThan(0);
    }
}
