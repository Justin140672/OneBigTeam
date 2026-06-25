using FluentValidation;

namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed class UpdatePositionProfileValidator : AbstractValidator<UpdatePositionProfileRequest>
{
    public UpdatePositionProfileValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Id)
            .NotEmpty();

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000)
            .When(r => r.Description is not null);

        RuleFor(r => r.ProbationMonthsOverride)
            .InclusiveBetween(1, 24)
            .When(r => r.ProbationMonthsOverride.HasValue)
            .WithMessage("ProbationMonthsOverride must be between 1 and 24.");
    }
}
