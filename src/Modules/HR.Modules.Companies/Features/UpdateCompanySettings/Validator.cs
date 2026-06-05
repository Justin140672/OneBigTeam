using FluentValidation;

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

		RuleFor(request => request.WorkingWeek)
			.NotEmpty()
			.MaximumLength(30);

		RuleFor(request => request.LeaveYearStartMonth)
			.InclusiveBetween(1, 12);

		RuleFor(request => request.DefaultHolidayAllowance)
			.GreaterThan(0)
			.LessThanOrEqualTo(365);

		RuleFor(request => request.ProbationMonths)
			.InclusiveBetween(0, 24);
	}
}
