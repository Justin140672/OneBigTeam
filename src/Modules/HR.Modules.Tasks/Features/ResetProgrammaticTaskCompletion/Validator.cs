using FluentValidation;

namespace HR.Modules.Tasks.Features.ResetProgrammaticTaskCompletion;

internal sealed class ResetProgrammaticTaskCompletionValidator : AbstractValidator<ResetProgrammaticTaskCompletionRequest>
{
    public ResetProgrammaticTaskCompletionValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.OperationId).NotEmpty();
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
    }
}
