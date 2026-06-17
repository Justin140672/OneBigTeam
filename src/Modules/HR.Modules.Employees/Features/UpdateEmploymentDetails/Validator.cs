using FluentValidation;
using HR.Modules.Employees.Domain;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.UpdateEmploymentDetails;

internal sealed class UpdateEmploymentDetailsValidator : AbstractValidator<UpdateEmploymentDetailsRequest>
{
    public UpdateEmploymentDetailsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.StartDate).NotEmpty();

        RuleFor(r => r.Status)
            .Must(s => s != EmploymentStatus.Draft)
            .WithMessage("Cannot set employment status to Draft.");

        RuleFor(r => r.EmployeeNumber)
            .MaximumLength(50)
            .When(r => !string.IsNullOrWhiteSpace(r.EmployeeNumber));

        RuleFor(r => r.Notes)
            .MaximumLength(4000)
            .When(r => !string.IsNullOrWhiteSpace(r.Notes));

        RuleFor(r => r.HoursPerDayOverride)
            .GreaterThan(0).LessThanOrEqualTo(24)
            .When(r => r.HoursPerDayOverride.HasValue);

        RuleFor(r => r.WorkingDaysOverride)
            .NotEqual(WorkingDays.None)
            .When(r => r.WorkingDaysOverride.HasValue);
    }
}
