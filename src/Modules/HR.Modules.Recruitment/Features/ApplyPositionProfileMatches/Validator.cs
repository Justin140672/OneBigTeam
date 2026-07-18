using FluentValidation;

namespace HR.Modules.Recruitment.Features.ApplyPositionProfileMatches;

internal sealed class ApplyPositionProfileMatchesValidator : AbstractValidator<ApplyPositionProfileMatchesRequest>
{
    public ApplyPositionProfileMatchesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
