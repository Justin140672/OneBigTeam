using FluentValidation;

namespace HR.Modules.Recruitment.Features.UpdateInterview;

internal sealed class UpdateInterviewValidator : AbstractValidator<UpdateInterviewRequest>
{
    public UpdateInterviewValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();

        RuleFor(r => r.InterviewId)
            .NotEmpty();

        RuleFor(r => r.InterviewerEmployeeId)
            .NotEmpty();

        RuleFor(r => r.ScheduledAt)
            .NotEmpty();

        RuleFor(r => r.DurationMinutes)
            .GreaterThan(0)
            .When(r => r.DurationMinutes.HasValue);

        RuleFor(r => r.Location)
            .MaximumLength(200)
            .When(r => !string.IsNullOrWhiteSpace(r.Location));
    }
}
