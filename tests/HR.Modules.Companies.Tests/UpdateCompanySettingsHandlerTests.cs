using System.Text.Json;
using HR.Modules.Companies.Contracts.Events;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanySettingsHandlerTests
{
	[Fact]
	public async Task HandleAsync_Updates_Settings_And_Creates_Outbox_Message()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(),
			new FakeCurrentUser(null));

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				CompanyId = company.Id,
				TimeZone = "Europe/London",
				Locale = "en-GB",
				Version = 1,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.NotNull(result.Value);
		Assert.Equal("Europe/London", result.Value!.TimeZone);
		Assert.Equal("en-GB", result.Value.Locale);
		Assert.Equal(company.Id, result.Value.CompanyId);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal("Europe/London", savedSettings.TimeZone);
		Assert.Equal("en-GB", savedSettings.Locale);

		var outboxMessage = await context.OutboxMessages.SingleAsync();
		Assert.Equal(company.Id, outboxMessage.CompanyId);
		Assert.Equal("companies.company-settings.updated", outboxMessage.EventType);
		Assert.Equal("pending", outboxMessage.Status);
		Assert.Contains("Europe/London", outboxMessage.Payload);

		var integrationEvent = JsonSerializer.Deserialize<CompanySettingsUpdatedIntegrationEvent>(outboxMessage.Payload);
		Assert.NotNull(integrationEvent);
		Assert.Equal(company.Id, integrationEvent!.CompanyId);
		Assert.Equal("Europe/London", integrationEvent.TimeZone);
		Assert.Equal("en-GB", integrationEvent.Locale);
		Assert.Equal(new DateTimeOffset(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)), integrationEvent.OccurredAt);
	}

	[Fact]
	public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
	{
		await using var context = BuildContext();
		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(),
			new FakeCurrentUser(null));

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				CompanyId = Guid.NewGuid(),
				TimeZone = "UTC",
				Locale = "en-GB",
			},
			CancellationToken.None);

		Assert.True(result.IsFailure);
		Assert.Equal("not_found", result.Error.Code);
		Assert.Empty(context.OutboxMessages);
	}

	[Fact]
	public async Task HandleAsync_Publishes_CompanySettingsUpdatedAuditEvent_Scoped_To_TimeZone_And_Locale()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var auditPublisher = new CapturingAuditEventPublisher();
		var updateTime = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);
		var handler = new UpdateCompanySettingsHandler(context, new FakeClock(updateTime), auditPublisher, new FakeCurrentUser(null));

		await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				CompanyId = company.Id,
				TimeZone = "Europe/London",
				Locale = "en-GB",
				Version = 1,
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<CompanySettingsUpdatedAuditEvent>(auditEvt);
		Assert.Equal(company.Id, auditEvent.CompanyId);
		Assert.Null(auditEvent.ActorUserId);
		Assert.Equal(new DateTimeOffset(updateTime, TimeSpan.Zero), auditEvent.OccurredAt);

		Assert.NotNull(auditEvent.PreviousSettings);
		Assert.Equal("UTC", auditEvent.PreviousSettings!.TimeZone);
		Assert.Equal("en-GB", auditEvent.PreviousSettings.Locale);

		Assert.Equal("Europe/London", auditEvent.CurrentSettings.TimeZone);
		Assert.Equal("en-GB", auditEvent.CurrentSettings.Locale);
	}

	[Fact]
	public async Task HandleAsync_Captures_CurrentUser_As_Actor_On_AuditEvent()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var actorUserId = Guid.NewGuid();
		var auditPublisher = new CapturingAuditEventPublisher();
		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			auditPublisher,
			new FakeCurrentUser(actorUserId));

		await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				CompanyId = company.Id,
				TimeZone = "Europe/London",
				Locale = "en-GB",
				Version = 1,
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<CompanySettingsUpdatedAuditEvent>(auditEvt);
		Assert.Equal(actorUserId, auditEvent.ActorUserId);
	}

	[Theory]
	[InlineData("Europe/London")]
	[InlineData("UTC")]
	public async Task HandleAsync_Normalises_TimeZone_To_Canonical_Id(string requestedTimeZone)
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(),
			new FakeCurrentUser(null));

		CompanySettingsValidation.TryResolveTimeZone(requestedTimeZone, out var expectedCanonicalId);

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				CompanyId = company.Id,
				TimeZone = requestedTimeZone,
				Locale = "en-GB",
				Version = 1,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(expectedCanonicalId, result.Value!.TimeZone);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal(expectedCanonicalId, savedSettings.TimeZone);
	}

	[Fact]
	public async Task HandleAsync_Returns_Conflict_And_Publishes_No_AuditEvent_When_Version_Is_Stale()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		// First update succeeds and bumps Version from 1 to 2.
		var firstHandler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(),
			new FakeCurrentUser(null));

		var firstResult = await firstHandler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				CompanyId = company.Id,
				TimeZone = "Europe/London",
				Locale = "en-GB",
				Version = 1,
			},
			CancellationToken.None);

		Assert.True(firstResult.IsSuccess);
		Assert.Equal(2, firstResult.Value!.Version);

		// Second attempt is submitted against the stale Version = 1 read before the first update.
		var auditPublisher = new CapturingAuditEventPublisher();
		var secondHandler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc)),
			auditPublisher,
			new FakeCurrentUser(null));

		var secondResult = await secondHandler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				CompanyId = company.Id,
				TimeZone = "UTC",
				Locale = "en-GB",
				Version = 1,
			},
			CancellationToken.None);

		Assert.True(secondResult.IsFailure);
		Assert.Equal("conflict", secondResult.Error.Code);
		Assert.Empty(auditPublisher.Published);
	}

	private static CompaniesDbContext BuildContext()
	{
		var options = new DbContextOptionsBuilder<CompaniesDbContext>()
			.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
			.Options;

		return new CompaniesDbContext(options);
	}
}
