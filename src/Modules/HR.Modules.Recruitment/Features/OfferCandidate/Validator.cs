using FluentValidation;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed class OfferCandidateValidator : AbstractValidator<OfferCandidateRequest>
{
    public OfferCandidateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();

        RuleFor(r => r.OfferedSalary)
            .GreaterThan(0m).WithMessage("Offered salary must be greater than zero.")
            .When(r => r.OfferedSalary.HasValue);

        RuleFor(r => r.OfferedSalaryFrequency)
            .Must(f => Enum.TryParse<OfferSalaryFrequency>(f, ignoreCase: true, out var parsed)
                       && Enum.IsDefined(parsed))
            .WithMessage("Offered salary frequency must be one of: Annual, Hourly, Daily.")
            .When(r => !string.IsNullOrWhiteSpace(r.OfferedSalaryFrequency));

        RuleFor(r => r.OfferNotes)
            .MaximumLength(2000);
    }
}
