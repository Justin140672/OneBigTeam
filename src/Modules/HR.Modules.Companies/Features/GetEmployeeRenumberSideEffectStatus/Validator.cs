using FluentValidation;

namespace HR.Modules.Companies.Features.GetEmployeeRenumberSideEffectStatus;

internal sealed class GetEmployeeRenumberSideEffectStatusValidator : AbstractValidator<GetEmployeeRenumberSideEffectStatusRequest>
{
    public GetEmployeeRenumberSideEffectStatusValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.OutboxMessageId).NotEmpty();
    }
}
