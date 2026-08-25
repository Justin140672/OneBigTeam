using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateHrSettings;

internal sealed class UpdateHrSettingsHandler
{
	private readonly CompaniesDbContext _dbContext;
	private readonly IClock _clock;
	private readonly IAuditEventPublisher _auditEventPublisher;
	private readonly IEmployeeRenumberingService _employeeRenumberingService;
	private readonly ICurrentUser _currentUser;

	public UpdateHrSettingsHandler(
		CompaniesDbContext dbContext,
		IClock clock,
		IAuditEventPublisher auditEventPublisher,
		IEmployeeRenumberingService employeeRenumberingService,
		ICurrentUser currentUser)
	{
		_dbContext = dbContext;
		_clock = clock;
		_auditEventPublisher = auditEventPublisher;
		_employeeRenumberingService = employeeRenumberingService;
		_currentUser = currentUser;
	}

	public async Task<Result<UpdateHrSettingsResponse>> HandleAsync(
		UpdateHrSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var company = await _dbContext.Companies
			.Include(currentCompany => currentCompany.Settings)
			.SingleOrDefaultAsync(currentCompany => currentCompany.Id == request.CompanyId, cancellationToken);

		if (company is null)
		{
			return Result.Failure<UpdateHrSettingsResponse>(
				Error.NotFound($"Company with id '{request.CompanyId}' was not found."));
		}

		var now = _clock.UtcNowOffset();
		var previousSettings = company.Settings is null
			? null
			: new HrSettingsAuditSnapshot(
				company.Settings.WorkingDays,
				company.Settings.HoursPerDay,
				company.Settings.LeaveYearStartMonth,
				company.Settings.DefaultHolidayAllowance,
				company.Settings.ProbationMonths,
				company.Settings.ExcludePublicHolidaysFromLeave,
				company.Settings.ExcludePublicHolidaysFromSickness,
				company.Settings.DisplaySalaryOnEmployeeProfile,
				company.Settings.FitNoteRequiredAfterDays,
				company.Settings.ReturnToWorkRequiredAfterDays,
				company.Settings.DefaultAcknowledgementStatement,
				company.Settings.AcknowledgementReminderIntervalDays,
				company.Settings.NoticePeriodUnit,
				company.Settings.NoticePeriodLength,
				company.Settings.AutoDisableAccessOnLeavingDate,
				company.Settings.EmployeeNumberMode,
				company.Settings.EmployeeNumberPrefix,
				company.Settings.NextEmployeeNumber,
				company.Settings.EmployeeNumberMinimumLength,
				company.Settings.AssetNumberMode,
				company.Settings.AssetNumberPrefix,
				company.Settings.NextAssetNumber,
				company.Settings.AssetNumberMinimumLength,
				company.Settings.ProbationCheckpointDay1,
				company.Settings.ProbationCheckpointDay2,
				company.Settings.ProbationCheckpointDay3,
				company.Settings.FrequentAbsenceCountThreshold,
				company.Settings.FrequentAbsenceWindowDays,
				company.Settings.LongAbsenceDayThreshold,
				company.Settings.WeekdayPatternOccurrenceThreshold,
				company.Settings.WeekdayPatternWindowDays);

		var previousEmployeeNumberMode = company.Settings?.EmployeeNumberMode;
		var previousEmployeeNumberPrefix = company.Settings?.EmployeeNumberPrefix;
		var previousEmployeeNumberMinimumLength = company.Settings?.EmployeeNumberMinimumLength;

		var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
		settings.UpdateHrPolicy(
			request.WorkingDays,
			request.HoursPerDay,
			request.LeaveYearStartMonth,
			request.DefaultHolidayAllowance,
			request.ProbationMonths,
			request.ExcludePublicHolidaysFromLeave,
			request.ExcludePublicHolidaysFromSickness,
			request.DisplaySalaryOnEmployeeProfile,
			request.FitNoteRequiredAfterDays,
			request.ReturnToWorkRequiredAfterDays,
			request.DefaultAcknowledgementStatement.Trim(),
			request.AcknowledgementReminderIntervalDays,
			request.NoticePeriodUnit,
			request.NoticePeriodLength,
			request.AutoDisableAccessOnLeavingDate,
			request.EmployeeNumberMode,
			request.EmployeeNumberPrefix,
			request.NextEmployeeNumber,
			request.EmployeeNumberMinimumLength,
			now);

		settings.UpdateAssetNumberSettings(
			request.AssetNumberMode,
			request.AssetNumberPrefix,
			request.NextAssetNumber,
			request.AssetNumberMinimumLength,
			now);

		settings.UpdateProbationCheckpoints(
			request.ProbationCheckpointDay1,
			request.ProbationCheckpointDay2,
			request.ProbationCheckpointDay3,
			now);

		settings.UpdateAttendanceAlertThresholds(
			request.FrequentAbsenceCountThreshold,
			request.FrequentAbsenceWindowDays,
			request.LongAbsenceDayThreshold,
			request.WeekdayPatternOccurrenceThreshold,
			request.WeekdayPatternWindowDays,
			now);

		company.SetSettings(settings, now);

		// SET-03: same forced-OriginalValue concurrency check as UpdateCompanySettingsHandler —
		// both slices mutate the same CompanySettings row and share one version counter.
		_dbContext.Entry(settings).Property(s => s.Version).OriginalValue = request.Version;

		try
		{
			await _dbContext.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			// Nothing has committed (SaveChangesAsync throws before the transaction commits), so no
			// renumbering call below runs and no audit/integration event is published for this
			// rejected attempt.
			return Result.Failure<UpdateHrSettingsResponse>(
				Error.Conflict("HR settings were changed by someone else. Reload the latest settings and try again."));
		}

		// Format-change renumbering (item 27): only triggered when the format actually changed
		// while the company STAYS in Automatic mode — never on a Manual<->Automatic mode switch,
		// and never for a Manual-mode company (nothing to renumber to).
		var formatChanged =
			previousEmployeeNumberPrefix != settings.EmployeeNumberPrefix ||
			previousEmployeeNumberMinimumLength != settings.EmployeeNumberMinimumLength;

		if (formatChanged &&
			previousEmployeeNumberMode == EmployeeNumberMode.Automatic &&
			settings.EmployeeNumberMode == EmployeeNumberMode.Automatic)
		{
			await _employeeRenumberingService.RenumberAllEmployeesAsync(company.Id, cancellationToken);
		}

		await _auditEventPublisher.PublishAsync(
			new HrSettingsUpdatedAuditEvent(
				company.Id,
				_currentUser.UserId,
				now,
				previousSettings,
				new HrSettingsAuditSnapshot(
					settings.WorkingDays,
					settings.HoursPerDay,
					settings.LeaveYearStartMonth,
					settings.DefaultHolidayAllowance,
					settings.ProbationMonths,
					settings.ExcludePublicHolidaysFromLeave,
					settings.ExcludePublicHolidaysFromSickness,
					settings.DisplaySalaryOnEmployeeProfile,
					settings.FitNoteRequiredAfterDays,
					settings.ReturnToWorkRequiredAfterDays,
					settings.DefaultAcknowledgementStatement,
					settings.AcknowledgementReminderIntervalDays,
					settings.NoticePeriodUnit,
					settings.NoticePeriodLength,
					settings.AutoDisableAccessOnLeavingDate,
					settings.EmployeeNumberMode,
					settings.EmployeeNumberPrefix,
					settings.NextEmployeeNumber,
					settings.EmployeeNumberMinimumLength,
					settings.AssetNumberMode,
					settings.AssetNumberPrefix,
					settings.NextAssetNumber,
					settings.AssetNumberMinimumLength,
					settings.ProbationCheckpointDay1,
					settings.ProbationCheckpointDay2,
					settings.ProbationCheckpointDay3,
					settings.FrequentAbsenceCountThreshold,
					settings.FrequentAbsenceWindowDays,
					settings.LongAbsenceDayThreshold,
					settings.WeekdayPatternOccurrenceThreshold,
					settings.WeekdayPatternWindowDays)),
			cancellationToken);

		return Result.Success(new UpdateHrSettingsResponse(
			company.Id,
			settings.WorkingDays,
			settings.HoursPerDay,
			settings.LeaveYearStartMonth,
			settings.DefaultHolidayAllowance,
			settings.ProbationMonths,
			settings.ExcludePublicHolidaysFromLeave,
			settings.ExcludePublicHolidaysFromSickness,
			settings.DisplaySalaryOnEmployeeProfile,
			settings.FitNoteRequiredAfterDays,
			settings.ReturnToWorkRequiredAfterDays,
			settings.DefaultAcknowledgementStatement,
			settings.AcknowledgementReminderIntervalDays,
			settings.NoticePeriodUnit,
			settings.NoticePeriodLength,
			settings.AutoDisableAccessOnLeavingDate,
			settings.EmployeeNumberMode,
			settings.EmployeeNumberPrefix,
			settings.NextEmployeeNumber,
			settings.EmployeeNumberMinimumLength,
			settings.AssetNumberMode,
			settings.AssetNumberPrefix,
			settings.NextAssetNumber,
			settings.AssetNumberMinimumLength,
			settings.UpdatedAt,
			settings.Version,
			settings.ProbationCheckpointDay1,
			settings.ProbationCheckpointDay2,
			settings.ProbationCheckpointDay3,
			settings.FrequentAbsenceCountThreshold,
			settings.FrequentAbsenceWindowDays,
			settings.LongAbsenceDayThreshold,
			settings.WeekdayPatternOccurrenceThreshold,
			settings.WeekdayPatternWindowDays));
	}
}
