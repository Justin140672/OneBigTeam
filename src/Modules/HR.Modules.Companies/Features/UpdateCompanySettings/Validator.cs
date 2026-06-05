using FluentValidation;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed class UpdateCompanySettingsValidator : AbstractValidator<UpdateCompanySettingsRequest>
{
	private const WorkingDays AllowedWorkingDays =
		WorkingDays.Monday
		| WorkingDays.Tuesday
		| WorkingDays.Wednesday
		| WorkingDays.Thursday
		| WorkingDays.Friday
		| WorkingDays.Saturday
		| WorkingDays.Sunday;

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

		RuleFor(request => request.WorkingWeek)
			.Must(workingDays =>
				workingDays != WorkingDays.None
				&& (workingDays & ~AllowedWorkingDays) == 0)
			.WithMessage("Working week must include at least one valid day.");

		RuleFor(request => request.LeaveYearStartMonth)
			.InclusiveBetween(1, 12);

		RuleFor(request => request.DefaultHolidayAllowance)
			.GreaterThanOrEqualTo(0m)
			.LessThanOrEqualTo(999.99m);

		RuleFor(request => request.ProbationMonths)
			.InclusiveBetween(0, 36);
	}
}
