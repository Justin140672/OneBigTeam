using FluentValidation;

namespace HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;

internal sealed class RetryEmployeeRenumberSideEffectValidator : AbstractValidator<RetryEmployeeRenumberSideEffectRequest>
{
    public RetryEmployeeRenumberSideEffectValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.OutboxMessageId).NotEmpty();
    }
}
