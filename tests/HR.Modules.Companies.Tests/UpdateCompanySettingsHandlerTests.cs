using System.Text.Json;
using HR.Modules.Companies.Contracts.Events;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanySettingsHandlerTests
{
	[Fact]
	public async Task HandleAsync_Updates_Settings_And_Creates_Outbox_Message()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", "acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new LoggerCompanyAuditEventPublisher(Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggerCompanyAuditEventPublisher>.Instance));

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "Europe/London",
				Locale = "en-GB",
				WorkingWeek = "Monday-Friday",
				LeaveYearStartMonth = 4,
				DefaultHolidayAllowance = 28,
				ProbationMonths = 3,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.NotNull(result.Value);
		Assert.Equal("Europe/London", result.Value!.TimeZone);
		Assert.Equal(28, result.Value.DefaultHolidayAllowance);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal("Europe/London", savedSettings.TimeZone);
		Assert.Equal(4, savedSettings.LeaveYearStartMonth);

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
		Assert.Equal("Monday-Friday", integrationEvent.WorkingWeek);
		Assert.Equal(4, integrationEvent.LeaveYearStartMonth);
		Assert.Equal(28, integrationEvent.DefaultHolidayAllowance);
		Assert.Equal(3, integrationEvent.ProbationMonths);
		Assert.Equal(new DateTimeOffset(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)), integrationEvent.OccurredAt);
	}

	[Fact]
	public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
	{
		await using var context = BuildContext();
		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new LoggerCompanyAuditEventPublisher(Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggerCompanyAuditEventPublisher>.Instance));

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = Guid.NewGuid(),
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingWeek = "Monday-Friday",
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
			},
			CancellationToken.None);

		Assert.True(result.IsFailure);
		Assert.Equal("not_found", result.Error.Code);
		Assert.Empty(context.OutboxMessages);
	}

	private static CompaniesDbContext BuildContext()
	{
		var options = new DbContextOptionsBuilder<CompaniesDbContext>()
			.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
			.Options;

		return new CompaniesDbContext(options);
	}
}
