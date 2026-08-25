using FluentValidation;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Features.UpdateHrSettings;

internal sealed class UpdateHrSettingsValidator : AbstractValidator<UpdateHrSettingsRequest>
{
	public UpdateHrSettingsValidator()
	{
		RuleFor(request => request.CompanyId)
			.NotEmpty();

		const WorkingDays AllDefinedWorkingDays = WorkingDays.Monday | WorkingDays.Tuesday |
			WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday |
			WorkingDays.Saturday | WorkingDays.Sunday;

		RuleFor(request => request.WorkingDays)
			.Must(w => w != WorkingDays.None)
			.WithMessage("At least one working day must be selected.")
			.Must(w => (w & ~AllDefinedWorkingDays) == WorkingDays.None)
			.WithMessage("Working days contains an undefined value.");

		RuleFor(request => request.HoursPerDay)
			.GreaterThan(0)
			.LessThanOrEqualTo(24);

		RuleFor(request => request.LeaveYearStartMonth)
			.InclusiveBetween(1, 12);

		RuleFor(request => request.DefaultHolidayAllowance)
			.GreaterThanOrEqualTo(0)
			.LessThanOrEqualTo(365);

		RuleFor(request => request.ProbationMonths)
			.GreaterThan(0)
			.LessThanOrEqualTo(24);

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

		RuleFor(request => request.NextAssetNumber)
			.GreaterThan(0);

		RuleFor(request => request.AssetNumberMinimumLength)
			.InclusiveBetween(1, 10);

		RuleFor(request => request.AssetNumberPrefix)
			.MaximumLength(20)
			.When(request => !string.IsNullOrWhiteSpace(request.AssetNumberPrefix));

		// SET-04: probation checkpoints. Each configured checkpoint must be positive; the set of
		// configured checkpoints (nulls are simply "disabled" and skipped) must be strictly
		// increasing and fall before the approximate probation end point (ProbationMonths * 30).
		RuleFor(request => request.ProbationCheckpointDay1)
			.GreaterThan(0)
			.When(request => request.ProbationCheckpointDay1.HasValue);

		RuleFor(request => request.ProbationCheckpointDay2)
			.GreaterThan(0)
			.When(request => request.ProbationCheckpointDay2.HasValue);

		RuleFor(request => request.ProbationCheckpointDay3)
			.GreaterThan(0)
			.When(request => request.ProbationCheckpointDay3.HasValue);

		RuleFor(request => request)
			.Must(request =>
			{
				var configured = new[]
				{
					request.ProbationCheckpointDay1,
					request.ProbationCheckpointDay2,
					request.ProbationCheckpointDay3,
				}.Where(day => day.HasValue).Select(day => day!.Value).ToList();

				if (configured.Count != configured.Distinct().Count())
				{
					return false;
				}

				for (var i = 1; i < configured.Count; i++)
				{
					if (configured[i] <= configured[i - 1])
					{
						return false;
					}
				}

				var probationEndDay = request.ProbationMonths * 30;
				return configured.All(day => day < probationEndDay);
			})
			.WithMessage("Probation checkpoints must be unique, strictly increasing, and fall before the end of probation.")
			.WithName("ProbationCheckpoints");

		RuleFor(request => request.FrequentAbsenceCountThreshold)
			.GreaterThan(0)
			.LessThanOrEqualTo(50);

		RuleFor(request => request.FrequentAbsenceWindowDays)
			.InclusiveBetween(30, 730);

		RuleFor(request => request.LongAbsenceDayThreshold)
			.GreaterThan(0)
			.LessThanOrEqualTo(365);

		RuleFor(request => request.WeekdayPatternOccurrenceThreshold)
			.GreaterThan(0)
			.LessThanOrEqualTo(50);

		RuleFor(request => request.WeekdayPatternWindowDays)
			.InclusiveBetween(30, 730);
	}
}
