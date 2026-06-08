using FluentValidation;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfile;

internal sealed class UpdateEmployeeProfileValidator : AbstractValidator<UpdateEmployeeProfileRequest>
{
    public UpdateEmployeeProfileValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Id)
            .NotEmpty();

        RuleFor(r => r.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.WorkEmail)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();

        RuleFor(r => r.PersonalEmail)
            .MaximumLength(320)
            .EmailAddress()
            .When(r => !string.IsNullOrWhiteSpace(r.PersonalEmail));

        RuleFor(r => r.StartDate)
            .NotEmpty();
    }
}
