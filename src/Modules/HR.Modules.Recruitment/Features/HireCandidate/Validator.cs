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

        // NotEmpty is intentionally not enforced here: in Automatic employee-numbering mode the
        // request may omit EmployeeNumber entirely — HireCandidateHandler forwards it through to
        // IEmployeeProvisioningService/CreateEmployeeHandler, which already generates one in that
        // case (and enforces requiredness itself in Manual mode, since that depends on a company
        // settings read this validator does not perform). Mirrors CreateEmployeeValidator's own
        // EmployeeNumber rule exactly.
        RuleFor(r => r.EmployeeNumber)
            .MaximumLength(50);

        RuleFor(r => r.EmploymentTypeId)
            .NotEmpty().WithMessage("Employment type is required.");

        // Mirrors the max lengths enforced on Employee.AddressLine1/AddressLine2/City/County/
        // PostCode (EmployeeConfiguration) — all optional here, same as on Employee Edit.
        RuleFor(r => r.AddressLine1).MaximumLength(200);
        RuleFor(r => r.AddressLine2).MaximumLength(200);
        RuleFor(r => r.City).MaximumLength(100);
        RuleFor(r => r.County).MaximumLength(100);
        RuleFor(r => r.PostCode).MaximumLength(20);
    }
}
