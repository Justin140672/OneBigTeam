using FluentValidation;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.RecordInterviewOutcome;

internal sealed class RecordInterviewOutcomeValidator : AbstractValidator<RecordInterviewOutcomeRequest>
{
    public RecordInterviewOutcomeValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();

        RuleFor(r => r.InterviewId)
            .NotEmpty();

        RuleFor(r => r.Outcome)
            .NotEqual(InterviewOutcome.Pending)
            .WithMessage("Outcome must not be 'Pending'.");

        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => !string.IsNullOrWhiteSpace(r.Notes));
    }
}
