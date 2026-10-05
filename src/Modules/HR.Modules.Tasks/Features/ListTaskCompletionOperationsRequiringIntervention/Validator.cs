using FluentValidation;
using HR.Modules.Tasks.Domain;

namespace HR.Modules.Tasks.Features.ListTaskCompletionOperationsRequiringIntervention;

internal sealed class ListTaskCompletionOperationsRequiringInterventionValidator
    : AbstractValidator<ListTaskCompletionOperationsRequiringInterventionRequest>
{
    public static readonly string[] AllowedStatuses =
    [
        TaskCompletionOperation.StatusEffectsTerminalFailure,
        TaskCompletionOperation.StatusDataIntegrityFailure,
        TaskCompletionOperation.StatusEffectsVerified,
        TaskCompletionOperation.StatusWaived,
    ];

    public ListTaskCompletionOperationsRequiringInterventionValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
        RuleFor(r => r.FailureCategory).MaximumLength(64);
        RuleFor(r => r.Status)
            .Must(s => s is null || AllowedStatuses.Contains(s))
            .WithMessage("Status must be one of: " + string.Join(", ", AllowedStatuses) + ".");
    }
}
