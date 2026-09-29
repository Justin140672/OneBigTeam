using System.Text.Json;
using HR.Modules.Companies.Contracts.Events;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed class UpdateCompanySettingsHandler
{
	private readonly CompaniesDbContext _dbContext;
	private readonly IClock _clock;
	private readonly IAuditEventPublisher _auditEventPublisher;
	private readonly ICurrentUser _currentUser;
	private readonly IExecutionContextAccessor? _executionContextAccessor;

	public UpdateCompanySettingsHandler(
		CompaniesDbContext dbContext,
		IClock clock,
		IAuditEventPublisher auditEventPublisher,
		ICurrentUser currentUser,
		// Ticket 23 (P2): reference wiring for Companies - optional so existing direct unit-test
		// constructions are unaffected; production DI always supplies the real singleton.
		IExecutionContextAccessor? executionContextAccessor = null)
	{
		_dbContext = dbContext;
		_clock = clock;
		_auditEventPublisher = auditEventPublisher;
		_currentUser = currentUser;
		_executionContextAccessor = executionContextAccessor;
	}

	public async Task<Result<UpdateCompanySettingsResponse>> HandleAsync(
		UpdateCompanySettingsRequest request,
		CancellationToken cancellationToken)
	{
		var company = await _dbContext.Companies
			.Include(currentCompany => currentCompany.Settings)
			.SingleOrDefaultAsync(currentCompany => currentCompany.Id == request.CompanyId, cancellationToken);

		if (company is null)
		{
			return Result.Failure<UpdateCompanySettingsResponse>(
				Error.NotFound($"Company with id '{request.CompanyId}' was not found."));
		}

		var now = _clock.UtcNowOffset();

		if (company.Settings is not null && company.Settings.Version != request.Version)
		{
			return Result.Failure<UpdateCompanySettingsResponse>(
				Error.Conflict("Company settings were changed by someone else. Reload the latest settings and try again."));
		}

		var previousSettings = company.Settings is null
			? null
			: new CompanySettingsAuditSnapshot(
				company.Settings.TimeZone,
				company.Settings.Locale);

		CompanySettingsValidation.TryResolveTimeZone(request.TimeZone, out var canonicalTimeZone);

		var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
		settings.UpdateCompanyProfile(
			canonicalTimeZone,
			request.Locale.Trim(),
			now);

		company.SetSettings(settings, now);

		_dbContext.Entry(settings).Property(s => s.Version).OriginalValue = request.Version;

		var payload = JsonSerializer.Serialize(new CompanySettingsUpdatedIntegrationEvent(
			company.Id,
			settings.TimeZone,
			settings.Locale,
			now));

		var outboxMessage = OutboxMessage.CreatePending(
			Guid.NewGuid(),
			company.Id,
			"companies.company-settings.updated",
			payload,
			now,
			_executionContextAccessor?.Current);

		_dbContext.OutboxMessages.Add(outboxMessage);

		try
		{
			await _dbContext.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			return Result.Failure<UpdateCompanySettingsResponse>(
				Error.Conflict("Company settings were changed by someone else. Reload the latest settings and try again."));
		}

		await _auditEventPublisher.PublishAsync(
			new CompanySettingsUpdatedAuditEvent(
				company.Id,
				_currentUser.UserId,
				now,
				previousSettings,
				new CompanySettingsAuditSnapshot(
					settings.TimeZone,
					settings.Locale)),
			cancellationToken);

		return Result.Success(new UpdateCompanySettingsResponse(
			company.Id,
			settings.TimeZone,
			settings.Locale,
			settings.UpdatedAt,
			settings.Version));
	}
}
