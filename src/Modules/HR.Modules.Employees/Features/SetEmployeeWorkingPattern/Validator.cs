using FluentValidation;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.SetEmployeeWorkingPattern;

internal sealed class SetEmployeeWorkingPatternValidator : AbstractValidator<SetEmployeeWorkingPatternRequest>
{
    public SetEmployeeWorkingPatternValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();

        When(r => r.WorkingDaysOverride.HasValue, () =>
        {
            RuleFor(r => r.WorkingDaysOverride!.Value)
                .Must(w => w != WorkingDays.None)
                .WithMessage("Working days override must include at least one day.");
        });

        When(r => r.HoursPerDayOverride.HasValue, () =>
        {
            RuleFor(r => r.HoursPerDayOverride!.Value)
                .GreaterThan(0)
                .LessThanOrEqualTo(24);
        });
    }
}
