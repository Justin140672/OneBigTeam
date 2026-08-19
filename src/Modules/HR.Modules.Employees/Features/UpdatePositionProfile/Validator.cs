using FluentValidation;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed class UpdatePositionProfileValidator : AbstractValidator<UpdatePositionProfileRequest>
{
    public UpdatePositionProfileValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Id)
            .NotEmpty();

        RuleFor(r => r.DepartmentId)
            .NotEmpty();

        RuleFor(r => r.LocationId)
            .NotEmpty();

        RuleFor(r => r.DefaultLeavePolicyId)
            .NotEmpty();

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000)
            .When(r => r.Description is not null);

        RuleFor(r => r.ProbationMonthsOverride)
            .InclusiveBetween(1, 24)
            .When(r => r.ProbationMonthsOverride.HasValue)
            .WithMessage("ProbationMonthsOverride must be between 1 and 24.");

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

        RuleFor(r => r)
            .Must(r => r.NoticePeriodUnitOverride.HasValue == r.NoticePeriodLengthOverride.HasValue)
            .WithMessage("Notice period unit and length overrides must both be set or both be empty.");

        RuleFor(r => r.NoticePeriodLengthOverride)
            .GreaterThan(0)
            .When(r => r.NoticePeriodLengthOverride.HasValue)
            .WithMessage("NoticePeriodLengthOverride must be greater than 0.");

        RuleFor(r => r.SalaryMin)
            .GreaterThanOrEqualTo(0)
            .When(r => r.SalaryMin.HasValue);

        RuleFor(r => r.SalaryMax)
            .GreaterThanOrEqualTo(r => r.SalaryMin ?? 0)
            .When(r => r.SalaryMax.HasValue)
            .WithMessage("SalaryMax must be greater than or equal to SalaryMin.");

        RuleFor(r => r.SalaryType)
            .IsInEnum()
            .When(r => r.SalaryType.HasValue);
    }
}
