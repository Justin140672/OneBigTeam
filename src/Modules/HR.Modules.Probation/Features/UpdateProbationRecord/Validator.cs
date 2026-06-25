using FluentValidation;
using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Features.UpdateProbationRecord;

internal sealed class UpdateProbationRecordValidator : AbstractValidator<UpdateProbationRecordRequest>
{
    public UpdateProbationRecordValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.ManagerEmployeeId).NotEmpty();
        RuleFor(r => r.ExpectedEndDate).NotEmpty();
        RuleFor(r => r.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<ProbationStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be a valid probation status.");

        RuleFor(r => r.Notes).MaximumLength(2000).When(r => r.Notes is not null);
        RuleFor(r => r.OutcomeNotes).MaximumLength(2000).When(r => r.OutcomeNotes is not null);
        RuleFor(r => r.ExtensionReason).MaximumLength(1000).When(r => r.ExtensionReason is not null);

        RuleFor(r => r.ExtensionReason)
            .NotEmpty()
            .When(r => Enum.TryParse<ProbationStatus>(r.Status, ignoreCase: true, out var s) && s == ProbationStatus.Extended)
            .WithMessage("Extension reason is required when status is Extended.");

        RuleFor(r => r.DecisionMakerEmployeeId)
            .NotEmpty()
            .When(r => Enum.TryParse<ProbationStatus>(r.Status, ignoreCase: true, out var s) && s is ProbationStatus.Passed or ProbationStatus.Failed)
            .WithMessage("Decision maker is required when recording an outcome.");

        RuleFor(r => r.DecisionDate)
            .NotEmpty()
            .When(r => Enum.TryParse<ProbationStatus>(r.Status, ignoreCase: true, out var s) && s is ProbationStatus.Passed or ProbationStatus.Failed)
            .WithMessage("Decision date is required when recording an outcome.");
    }
}
