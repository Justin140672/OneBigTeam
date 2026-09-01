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
	private readonly ICurrentUser _currentUser;

	public UpdateCompanySettingsHandler(
		CompaniesDbContext dbContext,
		IClock clock,
		IAuditEventPublisher auditEventPublisher,
		ICurrentUser currentUser)
	{
		_dbContext = dbContext;
		_clock = clock;
		_auditEventPublisher = auditEventPublisher;
		_currentUser = currentUser;
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

		// SET-03: explicit optimistic-concurrency pre-check. The EF concurrency token on
		// CompanySettings.Version still guards the genuine write race, but when the version the
		// client submitted is already stale on read we can return a clean 409 here rather than
		// relying on the shape of the exception a batched 0-row UPDATE throws.
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

		// Validator already confirmed these resolve; normalise to the canonical time-zone id
		// before persistence so downstream TimeZoneInfo lookups are always consistent.
		CompanySettingsValidation.TryResolveTimeZone(request.TimeZone, out var canonicalTimeZone);

		var settings = company.Settings ?? CompanySettings.CreateDefault(company.Id, now);
		settings.UpdateCompanyProfile(
			canonicalTimeZone,
			request.Locale.Trim(),
			now);

		company.SetSettings(settings, now);

		// SET-03: force the concurrency check against the version the client actually read,
		// rather than whatever this handler's own SingleOrDefaultAsync just loaded a moment ago
		// (which would always match and never detect a conflict).
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
			now);

		_dbContext.OutboxMessages.Add(outboxMessage);

		try
		{
			await _dbContext.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			// No partial change and no audit/integration event: SaveChangesAsync throws before
			// anything commits, so the outbox message added above is rolled back with it.
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
