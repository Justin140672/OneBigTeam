using FluentValidation;

namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed class HireCandidateValidator : AbstractValidator<HireCandidateRequest>
{
    public HireCandidateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();

        RuleFor(r => r.StartDate)
            .NotEmpty();

        RuleFor(r => r.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required.");

        RuleFor(r => r.Nationality)
            .NotEmpty().WithMessage("Nationality is required.")
            .MaximumLength(100);

        RuleFor(r => r.Gender)
            .NotEmpty().WithMessage("Gender is required.")
            .MaximumLength(50);

        RuleFor(r => r.EmployeeNumber)
            .NotEmpty().WithMessage("Employee number is required.")
            .MaximumLength(50);

        RuleFor(r => r.EmploymentTypeId)
            .NotEmpty().WithMessage("Employment type is required.");

        RuleFor(r => r.DepartmentId)
            .NotEmpty().WithMessage("Department is required.");

        RuleFor(r => r.LocationId)
            .NotEmpty().WithMessage("Location is required.");

        RuleFor(r => r.PositionProfileId)
            .NotEmpty().WithMessage("Position profile is required.");
    }
}
