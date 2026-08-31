using FluentValidation;

namespace HR.Modules.Recruitment.Features.CreateRecruitmentStage;

internal sealed class CreateRecruitmentStageValidator : AbstractValidator<CreateRecruitmentStageRequest>
{
    public CreateRecruitmentStageValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.DisplayOrder)
            .GreaterThan(0);

        RuleFor(r => r.TerminalOutcome)
            .IsInEnum();

        RuleFor(r => r.Purpose)
            .IsInEnum()
            .When(r => r.Purpose.HasValue);

        // DSH-04: a purpose expresses a non-terminal metric role; terminal stages carry their
        // meaning through TerminalOutcome instead.
        RuleFor(r => r.Purpose)
            .Null()
            .When(r => r.IsTerminal)
            .WithMessage("A terminal recruitment stage cannot have a purpose.");
    }
}
