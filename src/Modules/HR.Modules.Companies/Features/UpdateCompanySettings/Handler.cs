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
			.SingleOrDefaultAsync(currentCompany => currentCompany.Id == request.CompanyId, cancellationToken);

		if (company is null)
		{
			return Result.Failure<UpdateCompanySettingsResponse>(
				Error.NotFound($"Company with id '{request.CompanyId}' was not found."));
		}

		var now = _clock.UtcNowOffset();
		var previousSettings = company.Settings is null
			? null
			: new CompanySettingsAuditSnapshot(
				company.Settings.TimeZone,
				company.Settings.Locale);

		var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
		settings.UpdateCompanyProfile(
			request.TimeZone.Trim(),
			request.Locale.Trim(),
			now);

		company.SetSettings(settings, now);

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
					settings.Locale)),
			cancellationToken);

		return Result.Success(new UpdateCompanySettingsResponse(
			company.Id,
			settings.TimeZone,
			settings.Locale,
			settings.UpdatedAt));
	}
}
