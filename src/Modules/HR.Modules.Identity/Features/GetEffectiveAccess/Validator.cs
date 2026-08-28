using FluentValidation;

namespace HR.Modules.Identity.Features.GetEffectiveAccess;

internal sealed class GetEffectiveAccessValidator : AbstractValidator<GetEffectiveAccessRequest>
{
    public GetEffectiveAccessValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
