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
            .NotEmpty().WithMessage("Offered salary frequency is required.")
            .Must(f => Enum.TryParse<OfferSalaryFrequency>(f, ignoreCase: true, out var parsed)
                       && Enum.IsDefined(parsed))
            .WithMessage("Offered salary frequency must be one of: Annual, Hourly, Daily.");

        RuleFor(r => r.OfferNotes)
            .MaximumLength(2000);

        RuleFor(r => r.Currency)
            .Length(3).WithMessage("Currency must be a 3-letter ISO 4217 code (e.g. GBP).")
            .When(r => !string.IsNullOrWhiteSpace(r.Currency));

        RuleFor(r => r.ProposedManagerId)
            .Must(id => id != Guid.Empty).WithMessage("Proposed manager is invalid.")
            .When(r => r.ProposedManagerId.HasValue);

        RuleFor(r => r.ProposedManagerId)
            .Null().WithMessage("A manager cannot be selected together with 'No manager'.")
            .When(r => r.NoManager);

        RuleFor(r => r.HoursPerWeek)
            .InclusiveBetween(0.01m, 168m)
            .When(r => r.HoursPerWeek.HasValue);

        RuleFor(r => r.Fte)
            .InclusiveBetween(0.001m, 1m)
            .When(r => r.Fte.HasValue);
    }
}
