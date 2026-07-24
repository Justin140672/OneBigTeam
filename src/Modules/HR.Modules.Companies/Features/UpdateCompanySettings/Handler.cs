using System.Text.Json;
using HR.Modules.Companies.Contracts.Events;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed class UpdateCompanySettingsHandler
{
	private readonly CompaniesDbContext _dbContext;
	private readonly IClock _clock;
	private readonly IAuditEventPublisher _auditEventPublisher;

	public UpdateCompanySettingsHandler(
		CompaniesDbContext dbContext,
		IClock clock,
		IAuditEventPublisher auditEventPublisher)
	{
		_dbContext = dbContext;
		_clock = clock;
		_auditEventPublisher = auditEventPublisher;
	}

	public async Task<Result<UpdateCompanySettingsResponse>> HandleAsync(
		UpdateCompanySettingsRequest request,
		CancellationToken cancellationToken)
	{
		var company = await _dbContext.Companies
			.Include(currentCompany => currentCompany.Settings)
			.SingleOrDefaultAsync(currentCompany => currentCompany.Id == request.Id, cancellationToken);

		if (company is null)
		{
			return Result.Failure<UpdateCompanySettingsResponse>(
				Error.NotFound($"Company with id '{request.Id}' was not found."));
		}

		var now = _clock.UtcNowOffset();
		var previousSettings = company.Settings is null
			? null
			: new CompanySettingsAuditSnapshot(
				company.Settings.TimeZone,
				company.Settings.Locale,
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
				company.Settings.AcknowledgementReminderIntervalDays);

		var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
		settings.Update(
			request.TimeZone.Trim(),
			request.Locale.Trim(),
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
			now);

		company.SetSettings(settings, now);

		var payload = JsonSerializer.Serialize(new CompanySettingsUpdatedIntegrationEvent(
			company.Id,
			settings.TimeZone,
			settings.Locale,
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
			now));

		var outboxMessage = OutboxMessage.CreatePending(
			Guid.NewGuid(),
			company.Id,
			"companies.company-settings.updated",
			payload,
			now);

		_dbContext.OutboxMessages.Add(outboxMessage);
		await _dbContext.SaveChangesAsync(cancellationToken);

		await _auditEventPublisher.PublishAsync(
			new CompanySettingsUpdatedAuditEvent(
				company.Id,
				null,
				now,
				previousSettings,
				new CompanySettingsAuditSnapshot(
					settings.TimeZone,
					settings.Locale,
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
					settings.AcknowledgementReminderIntervalDays)),
			cancellationToken);

		return Result.Success(new UpdateCompanySettingsResponse(
			company.Id,
			settings.TimeZone,
			settings.Locale,
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
			settings.UpdatedAt));
	}
}
