using FluentValidation;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed class UpdateCompanySettingsValidator : AbstractValidator<UpdateCompanySettingsRequest>
{
	public UpdateCompanySettingsValidator()
	{
		RuleFor(request => request.Id)
			.NotEmpty();

		RuleFor(request => request.TimeZone)
			.NotEmpty()
			.MaximumLength(100);

		RuleFor(request => request.Locale)
			.NotEmpty()
			.MaximumLength(20);

		RuleFor(request => request.WorkingDays)
			.Must(w => w != WorkingDays.None)
			.WithMessage("At least one working day must be selected.");

		RuleFor(request => request.HoursPerDay)
			.GreaterThan(0)
			.LessThanOrEqualTo(24);

		RuleFor(request => request.LeaveYearStartMonth)
			.InclusiveBetween(1, 12);

		RuleFor(request => request.DefaultHolidayAllowance)
			.GreaterThan(0)
			.LessThanOrEqualTo(365);

		RuleFor(request => request.ProbationMonths)
			.InclusiveBetween(0, 24);
	}
}
