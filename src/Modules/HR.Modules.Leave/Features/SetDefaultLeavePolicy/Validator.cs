using FluentValidation;

namespace HR.Modules.Leave.Features.SetDefaultLeavePolicy;

internal sealed class SetDefaultLeavePolicyValidator : AbstractValidator<SetDefaultLeavePolicyRequest>
{
    public SetDefaultLeavePolicyValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}
