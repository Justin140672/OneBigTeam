using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateHrSettings;

internal sealed class UpdateHrSettingsHandler
{
	private readonly CompaniesDbContext _dbContext;
	private readonly IClock _clock;
	private readonly IAuditEventPublisher _auditEventPublisher;

	public UpdateHrSettingsHandler(
		CompaniesDbContext dbContext,
		IClock clock,
		IAuditEventPublisher auditEventPublisher)
	{
		_dbContext = dbContext;
		_clock = clock;
		_auditEventPublisher = auditEventPublisher;
	}

	public async Task<Result<UpdateHrSettingsResponse>> HandleAsync(
		UpdateHrSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var company = await _dbContext.Companies
			.Include(currentCompany => currentCompany.Settings)
			.SingleOrDefaultAsync(currentCompany => currentCompany.Id == request.Id, cancellationToken);

		if (company is null)
		{
			return Result.Failure<UpdateHrSettingsResponse>(
				Error.NotFound($"Company with id '{request.Id}' was not found."));
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
				company.Settings.EmployeeNumberMinimumLength);

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

		company.SetSettings(settings, now);

		await _dbContext.SaveChangesAsync(cancellationToken);

		await _auditEventPublisher.PublishAsync(
			new HrSettingsUpdatedAuditEvent(
				company.Id,
				null,
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
					settings.EmployeeNumberMinimumLength)),
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
			settings.UpdatedAt));
	}
}
