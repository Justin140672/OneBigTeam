using FluentValidation;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Services;

namespace HR.Modules.Tasks.Features.AdjudicateTaskCompletionOperation;

internal sealed class AdjudicateTaskCompletionOperationValidator : AbstractValidator<AdjudicateTaskCompletionOperationRequest>
{
    public AdjudicateTaskCompletionOperationValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.OperationId).NotEmpty();
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
        RuleFor(r => r.Resolution)
            .Must(TaskCompletionAdjudicator.IsKnownResolution)
            .WithMessage("Resolution must be evidence_retry, effects_verified or waived.");

        RuleFor(r => r.Evidence).NotNull()
            .When(r => r.Resolution == TaskCompletionOperation.ResolutionEvidenceRetry)
            .WithMessage("Evidence is required to retry with supplied evidence.");

        When(r => r.Evidence is not null, () =>
        {
            RuleFor(r => r.Evidence!.AssignedEmployeeId).NotEmpty()
                .When(r => r.Evidence!.NotificationRequired)
                .WithMessage("The assigned employee is required when a completion notification is required.");
            RuleFor(r => r.Evidence!.TaskTitle).MaximumLength(200);
        });

        When(r => r.Resolution == TaskCompletionOperation.ResolutionEvidenceRetry && r.Evidence is not null, () =>
        {
            RuleFor(r => r.Evidence!.CompletedAt).NotNull();
            RuleFor(r => r.Evidence!.PreviousTaskStatus)
                .Must(s => s is not null && TaskCompletionAdjudicator.AllowedPreviousStatuses.Contains(s))
                .WithMessage("Previous task status must be Open or InProgress.");
        });
    }
}
