using Hangfire;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Jobs;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateHrSettings;

internal sealed class UpdateHrSettingsHandler
{
	// SET-08: event type for the durable employee-renumbering side-effect instruction, and the
	// enforced "at most one in-flight per company" DB constraint (see OutboxMessageConfiguration).
	public const string EmployeeRenumberEventType = "employee-numbering.reformat-requested";

	private readonly CompaniesDbContext _dbContext;
	private readonly IClock _clock;
	private readonly IAuditEventPublisher _auditEventPublisher;
	private readonly IBackgroundJobClient _backgroundJobClient;
	private readonly ICurrentUser _currentUser;
	private readonly IExecutionContextAccessor? _executionContextAccessor;

	public UpdateHrSettingsHandler(
		CompaniesDbContext dbContext,
		IClock clock,
		IAuditEventPublisher auditEventPublisher,
		IBackgroundJobClient backgroundJobClient,
		ICurrentUser currentUser,
		// Ticket 23 (P2): reference wiring for Companies - optional so existing direct unit-test
		// constructions are unaffected; production DI always supplies the real singleton.
		IExecutionContextAccessor? executionContextAccessor = null)
	{
		_dbContext = dbContext;
		_clock = clock;
		_auditEventPublisher = auditEventPublisher;
		_backgroundJobClient = backgroundJobClient;
		_currentUser = currentUser;
		_executionContextAccessor = executionContextAccessor;
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
			request.NextEmployeeNumber ?? settings.NextEmployeeNumber,
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

		_dbContext.Entry(settings).Property(s => s.Version).OriginalValue = request.Version;

		var formatChanged =
			previousEmployeeNumberPrefix != settings.EmployeeNumberPrefix ||
			previousEmployeeNumberMinimumLength != settings.EmployeeNumberMinimumLength;

		var triggersRenumber =
			formatChanged &&
			previousEmployeeNumberMode == EmployeeNumberMode.Automatic &&
			settings.EmployeeNumberMode == EmployeeNumberMode.Automatic;

		Domain.OutboxMessage? renumberOutboxMessage = null;

		if (triggersRenumber)
		{
			var inFlight = await _dbContext.OutboxMessages.AnyAsync(
				m => m.CompanyId == company.Id &&
					 m.EventType == EmployeeRenumberEventType &&
					 (m.Status == Domain.OutboxMessage.StatusPending || m.Status == Domain.OutboxMessage.StatusProcessing),
				cancellationToken);

			if (inFlight)
			{
				return Result.Failure<UpdateHrSettingsResponse>(
					Error.Conflict("A previous employee number reformat is still processing. Wait for it to complete before changing the numbering format again."));
			}

			renumberOutboxMessage = Domain.OutboxMessage.CreatePending(
				Guid.NewGuid(),
				company.Id,
				EmployeeRenumberEventType,
				System.Text.Json.JsonSerializer.Serialize(new
				{
					previousPrefix = previousEmployeeNumberPrefix,
					newPrefix = settings.EmployeeNumberPrefix,
					previousMinimumLength = previousEmployeeNumberMinimumLength,
					newMinimumLength = settings.EmployeeNumberMinimumLength,
				}),
				now,
				_executionContextAccessor?.Current);
			_dbContext.OutboxMessages.Add(renumberOutboxMessage);
		}

		try
		{
			await _dbContext.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			// Nothing has committed (SaveChangesAsync throws before the transaction commits), so no
			// outbox row was created either, and no audit/integration event is published for this
			// rejected attempt.
			return Result.Failure<UpdateHrSettingsResponse>(
				Error.Conflict("HR settings were changed by someone else. Reload the latest settings and try again."));
		}

		if (renumberOutboxMessage is not null)
		{
			_backgroundJobClient.Enqueue<EmployeeRenumberSideEffectJob>(
				job => job.ProcessAsync(renumberOutboxMessage.Id, renumberOutboxMessage.CompanyId));
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
			settings.WeekdayPatternWindowDays,
			renumberOutboxMessage?.Id,
			renumberOutboxMessage?.Status));
	}
}
