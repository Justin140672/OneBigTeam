using System.Text.Json;
using HR.Modules.Companies.Contracts.Events;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
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
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "Europe/London",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
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
		Assert.Equal(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
		             WorkingDays.Thursday | WorkingDays.Friday, integrationEvent.WorkingDays);
		Assert.Equal(7.5m, integrationEvent.HoursPerDay);
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
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = Guid.NewGuid(),
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
			},
			CancellationToken.None);

		Assert.True(result.IsFailure);
		Assert.Equal("not_found", result.Error.Code);
		Assert.Empty(context.OutboxMessages);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task HandleAsync_Persists_ExcludePublicHolidaysFromLeave(bool excludePublicHolidays)
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 12, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
				ExcludePublicHolidaysFromLeave = excludePublicHolidays,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(excludePublicHolidays, result.Value!.ExcludePublicHolidaysFromLeave);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal(excludePublicHolidays, savedSettings.ExcludePublicHolidaysFromLeave);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task HandleAsync_Persists_DisplaySalaryOnEmployeeProfile(bool displaySalaryOnEmployeeProfile)
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 12, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
				DisplaySalaryOnEmployeeProfile = displaySalaryOnEmployeeProfile,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(displaySalaryOnEmployeeProfile, result.Value!.DisplaySalaryOnEmployeeProfile);

		var savedSettings2 = await context.CompanySettings.SingleAsync();
		Assert.Equal(displaySalaryOnEmployeeProfile, savedSettings2.DisplaySalaryOnEmployeeProfile);
	}

	[Fact]
	public async Task HandleAsync_Publishes_CompanySettingsUpdatedAuditEvent()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var auditPublisher = new CapturingAuditEventPublisher();
		var updateTime = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);
		var handler = new UpdateCompanySettingsHandler(context, new FakeClock(updateTime), auditPublisher);

		await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "Europe/London",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 4,
				DefaultHolidayAllowance = 28,
				ProbationMonths = 3,
				ExcludePublicHolidaysFromLeave = true
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<CompanySettingsUpdatedAuditEvent>(auditEvt);
		Assert.Equal(company.Id, auditEvent.CompanyId);
		Assert.Null(auditEvent.ActorId);
		Assert.Equal(new DateTimeOffset(updateTime, TimeSpan.Zero), auditEvent.OccurredAt);

		Assert.NotNull(auditEvent.PreviousSettings);
		Assert.Equal("UTC", auditEvent.PreviousSettings!.TimeZone);
		Assert.Equal(1, auditEvent.PreviousSettings.LeaveYearStartMonth);

		Assert.Equal("Europe/London", auditEvent.CurrentSettings.TimeZone);
		Assert.Equal("en-GB", auditEvent.CurrentSettings.Locale);
		Assert.Equal(4, auditEvent.CurrentSettings.LeaveYearStartMonth);
		Assert.Equal(28m, auditEvent.CurrentSettings.DefaultHolidayAllowance);
		Assert.Equal(3, auditEvent.CurrentSettings.ProbationMonths);
		Assert.True(auditEvent.CurrentSettings.ExcludePublicHolidaysFromLeave);
	}

	[Fact]
	public async Task HandleAsync_Persists_DefaultAcknowledgementStatement_And_Returns_It_In_Response()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 7, 19, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
				DefaultAcknowledgementStatement = "Please confirm you have read this policy.",
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal("Please confirm you have read this policy.", result.Value!.DefaultAcknowledgementStatement);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal("Please confirm you have read this policy.", savedSettings.DefaultAcknowledgementStatement);
	}

	[Fact]
	public async Task HandleAsync_Falls_Back_To_Hardcoded_Default_When_DefaultAcknowledgementStatement_Is_Blank()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 7, 19, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
				DefaultAcknowledgementStatement = "   ",
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(CompanySettings.DefaultAcknowledgementStatementText, result.Value!.DefaultAcknowledgementStatement);
	}

	[Fact]
	public async Task HandleAsync_Includes_DefaultAcknowledgementStatement_In_AuditEvent_BeforeAndAfter_Snapshots()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		var initialSettings = CompanySettings.CreateDefault(company.Id, now);
		company.SetSettings(initialSettings, now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var auditPublisher = new CapturingAuditEventPublisher();
		var updateTime = new DateTime(2026, 7, 19, 11, 0, 0, DateTimeKind.Utc);
		var handler = new UpdateCompanySettingsHandler(context, new FakeClock(updateTime), auditPublisher);

		await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
				DefaultAcknowledgementStatement = "New acknowledgement statement.",
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<CompanySettingsUpdatedAuditEvent>(auditEvt);

		Assert.NotNull(auditEvent.PreviousSettings);
		Assert.Equal(CompanySettings.DefaultAcknowledgementStatementText, auditEvent.PreviousSettings!.DefaultAcknowledgementStatement);
		Assert.Equal("New acknowledgement statement.", auditEvent.CurrentSettings.DefaultAcknowledgementStatement);
	}

	[Fact]
	public async Task HandleAsync_Persists_NoticePeriodSettings_And_Returns_Them_In_Response()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateCompanySettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
				NoticePeriodUnit = NoticePeriodUnit.Weeks,
				NoticePeriodLength = 4,
				AutoDisableAccessOnLeavingDate = false,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(NoticePeriodUnit.Weeks, result.Value!.NoticePeriodUnit);
		Assert.Equal(4, result.Value.NoticePeriodLength);
		Assert.False(result.Value.AutoDisableAccessOnLeavingDate);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal(NoticePeriodUnit.Weeks, savedSettings.NoticePeriodUnit);
		Assert.Equal(4, savedSettings.NoticePeriodLength);
		Assert.False(savedSettings.AutoDisableAccessOnLeavingDate);
	}

	[Fact]
	public async Task HandleAsync_Includes_NoticePeriodSettings_In_AuditEvent_BeforeAndAfter_Snapshots()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var auditPublisher = new CapturingAuditEventPublisher();
		var updateTime = new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc);
		var handler = new UpdateCompanySettingsHandler(context, new FakeClock(updateTime), auditPublisher);

		await handler.HandleAsync(
			new UpdateCompanySettingsRequest
			{
				Id = company.Id,
				TimeZone = "UTC",
				Locale = "en-GB",
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
				              WorkingDays.Thursday | WorkingDays.Friday,
				HoursPerDay = 7.5m,
				LeaveYearStartMonth = 1,
				DefaultHolidayAllowance = 25,
				ProbationMonths = 6,
				NoticePeriodUnit = NoticePeriodUnit.Weeks,
				NoticePeriodLength = 2,
				AutoDisableAccessOnLeavingDate = false,
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<CompanySettingsUpdatedAuditEvent>(auditEvt);

		Assert.NotNull(auditEvent.PreviousSettings);
		Assert.Equal(NoticePeriodUnit.Months, auditEvent.PreviousSettings!.NoticePeriodUnit);
		Assert.Equal(1, auditEvent.PreviousSettings.NoticePeriodLength);
		Assert.True(auditEvent.PreviousSettings.AutoDisableAccessOnLeavingDate);

		Assert.Equal(NoticePeriodUnit.Weeks, auditEvent.CurrentSettings.NoticePeriodUnit);
		Assert.Equal(2, auditEvent.CurrentSettings.NoticePeriodLength);
		Assert.False(auditEvent.CurrentSettings.AutoDisableAccessOnLeavingDate);
	}

	private static CompaniesDbContext BuildContext()
	{
		var options = new DbContextOptionsBuilder<CompaniesDbContext>()
			.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
			.Options;

		return new CompaniesDbContext(options);
	}
}
