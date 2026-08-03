using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateHrSettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateHrSettingsHandlerTests
{
	private static UpdateHrSettingsRequest ValidRequest(Guid companyId) => new()
	{
		Id = companyId,
		WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
		              WorkingDays.Thursday | WorkingDays.Friday,
		HoursPerDay = 7.5m,
		LeaveYearStartMonth = 1,
		DefaultHolidayAllowance = 25,
		ProbationMonths = 6,
	};

	[Fact]
	public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
	{
		await using var context = BuildContext();
		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(ValidRequest(Guid.NewGuid()), CancellationToken.None);

		Assert.True(result.IsFailure);
		Assert.Equal("not_found", result.Error.Code);
		Assert.Empty(context.OutboxMessages);
	}

	[Fact]
	public async Task HandleAsync_Does_Not_Create_Outbox_Message()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(ValidRequest(company.Id), CancellationToken.None);

		Assert.True(result.IsSuccess);
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

		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 12, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with { ExcludePublicHolidaysFromLeave = excludePublicHolidays },
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

		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 12, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with { DisplaySalaryOnEmployeeProfile = displaySalaryOnEmployeeProfile },
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(displaySalaryOnEmployeeProfile, result.Value!.DisplaySalaryOnEmployeeProfile);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal(displaySalaryOnEmployeeProfile, savedSettings.DisplaySalaryOnEmployeeProfile);
	}

	[Fact]
	public async Task HandleAsync_Publishes_HrSettingsUpdatedAuditEvent()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var auditPublisher = new CapturingAuditEventPublisher();
		var updateTime = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher);

		await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				LeaveYearStartMonth = 4,
				DefaultHolidayAllowance = 28,
				ProbationMonths = 3,
				ExcludePublicHolidaysFromLeave = true,
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<HrSettingsUpdatedAuditEvent>(auditEvt);
		Assert.Equal(company.Id, auditEvent.CompanyId);
		Assert.Null(auditEvent.ActorId);
		Assert.Equal(new DateTimeOffset(updateTime, TimeSpan.Zero), auditEvent.OccurredAt);

		Assert.NotNull(auditEvent.PreviousSettings);
		Assert.Equal(1, auditEvent.PreviousSettings!.LeaveYearStartMonth);

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

		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 7, 19, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with { DefaultAcknowledgementStatement = "Please confirm you have read this policy." },
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

		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 7, 19, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with { DefaultAcknowledgementStatement = "   " },
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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher);

		await handler.HandleAsync(
			ValidRequest(company.Id) with { DefaultAcknowledgementStatement = "New acknowledgement statement." },
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<HrSettingsUpdatedAuditEvent>(auditEvt);

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

		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher);

		await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				NoticePeriodUnit = NoticePeriodUnit.Weeks,
				NoticePeriodLength = 2,
				AutoDisableAccessOnLeavingDate = false,
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<HrSettingsUpdatedAuditEvent>(auditEvt);

		Assert.NotNull(auditEvent.PreviousSettings);
		Assert.Equal(NoticePeriodUnit.Months, auditEvent.PreviousSettings!.NoticePeriodUnit);
		Assert.Equal(1, auditEvent.PreviousSettings.NoticePeriodLength);
		Assert.True(auditEvent.PreviousSettings.AutoDisableAccessOnLeavingDate);

		Assert.Equal(NoticePeriodUnit.Weeks, auditEvent.CurrentSettings.NoticePeriodUnit);
		Assert.Equal(2, auditEvent.CurrentSettings.NoticePeriodLength);
		Assert.False(auditEvent.CurrentSettings.AutoDisableAccessOnLeavingDate);
	}

	[Fact]
	public async Task HandleAsync_Persists_EmployeeNumberSettings_And_Returns_Them_In_Response()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 7, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 7, 26, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher());

		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP-",
				NextEmployeeNumber = 125,
				EmployeeNumberMinimumLength = 5,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(EmployeeNumberMode.Automatic, result.Value!.EmployeeNumberMode);
		Assert.Equal("EMP-", result.Value.EmployeeNumberPrefix);
		Assert.Equal(125, result.Value.NextEmployeeNumber);
		Assert.Equal(5, result.Value.EmployeeNumberMinimumLength);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal(EmployeeNumberMode.Automatic, savedSettings.EmployeeNumberMode);
		Assert.Equal("EMP-", savedSettings.EmployeeNumberPrefix);
		Assert.Equal(125, savedSettings.NextEmployeeNumber);
		Assert.Equal(5, savedSettings.EmployeeNumberMinimumLength);
	}

	[Fact]
	public async Task HandleAsync_Includes_EmployeeNumberSettings_In_AuditEvent_BeforeAndAfter_Snapshots()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 7, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var auditPublisher = new CapturingAuditEventPublisher();
		var updateTime = new DateTime(2026, 7, 26, 11, 0, 0, DateTimeKind.Utc);
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher);

		await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP-",
				NextEmployeeNumber = 200,
				EmployeeNumberMinimumLength = 6,
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<HrSettingsUpdatedAuditEvent>(auditEvt);

		Assert.NotNull(auditEvent.PreviousSettings);
		Assert.Equal(EmployeeNumberMode.Manual, auditEvent.PreviousSettings!.EmployeeNumberMode);
		Assert.Null(auditEvent.PreviousSettings.EmployeeNumberPrefix);
		Assert.Equal(1, auditEvent.PreviousSettings.NextEmployeeNumber);
		Assert.Equal(1, auditEvent.PreviousSettings.EmployeeNumberMinimumLength);

		Assert.Equal(EmployeeNumberMode.Automatic, auditEvent.CurrentSettings.EmployeeNumberMode);
		Assert.Equal("EMP-", auditEvent.CurrentSettings.EmployeeNumberPrefix);
		Assert.Equal(200, auditEvent.CurrentSettings.NextEmployeeNumber);
		Assert.Equal(6, auditEvent.CurrentSettings.EmployeeNumberMinimumLength);
	}

	private static CompaniesDbContext BuildContext()
	{
		var options = new DbContextOptionsBuilder<CompaniesDbContext>()
			.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
			.Options;

		return new CompaniesDbContext(options);
	}
}
