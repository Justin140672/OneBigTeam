using FluentValidation;
using HR.Modules.Employees.Domain;
using HR.Infrastructure.Abstractions;
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
            .NotEmpty().WithMessage("Employee number is required.")
            .MaximumLength(50);

        RuleFor(r => r.EmploymentTypeId)
            .NotEqual(Guid.Empty).When(r => r.EmploymentTypeId.HasValue);

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
