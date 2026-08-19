using FluentValidation;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Features.UpdateHrSettings;

internal sealed class UpdateHrSettingsValidator : AbstractValidator<UpdateHrSettingsRequest>
{
	public UpdateHrSettingsValidator()
	{
		RuleFor(request => request.Id)
			.NotEmpty();

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

		RuleFor(request => request.FitNoteRequiredAfterDays)
			.GreaterThan(0);

		RuleFor(request => request.ReturnToWorkRequiredAfterDays)
			.GreaterThan(0);

		RuleFor(request => request.DefaultAcknowledgementStatement)
			.MaximumLength(2000);

		RuleFor(request => request.AcknowledgementReminderIntervalDays)
			.GreaterThanOrEqualTo(1);

		RuleFor(request => request.NoticePeriodLength)
			.GreaterThan(0);

		RuleFor(request => request.NextEmployeeNumber)
			.GreaterThan(0);

		RuleFor(request => request.EmployeeNumberMinimumLength)
			.InclusiveBetween(1, 10);

		RuleFor(request => request.EmployeeNumberPrefix)
			.MaximumLength(20)
			.When(request => !string.IsNullOrWhiteSpace(request.EmployeeNumberPrefix));
	}
}
