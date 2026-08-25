using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateHrSettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateHrSettingsHandlerTests
{
	private static UpdateHrSettingsRequest ValidRequest(Guid companyId) => new()
	{
		CompanyId = companyId,
		WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
		              WorkingDays.Thursday | WorkingDays.Friday,
		HoursPerDay = 7.5m,
		LeaveYearStartMonth = 1,
		DefaultHolidayAllowance = 25,
		ProbationMonths = 6,
		Version = 1,
	};

	[Fact]
	public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
	{
		await using var context = BuildContext();
		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
		Assert.Null(auditEvent.ActorUserId);
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
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

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
		Assert.Equal(EmployeeNumberMode.Automatic, auditEvent.PreviousSettings!.EmployeeNumberMode);
		Assert.Null(auditEvent.PreviousSettings.EmployeeNumberPrefix);
		Assert.Equal(1, auditEvent.PreviousSettings.NextEmployeeNumber);
		Assert.Equal(4, auditEvent.PreviousSettings.EmployeeNumberMinimumLength);

		Assert.Equal(EmployeeNumberMode.Automatic, auditEvent.CurrentSettings.EmployeeNumberMode);
		Assert.Equal("EMP-", auditEvent.CurrentSettings.EmployeeNumberPrefix);
		Assert.Equal(200, auditEvent.CurrentSettings.NextEmployeeNumber);
		Assert.Equal(6, auditEvent.CurrentSettings.EmployeeNumberMinimumLength);
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
		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			auditPublisher,
			new NoOpEmployeeRenumberingService(),
			new FakeCurrentUser(actorUserId));

		await handler.HandleAsync(ValidRequest(company.Id), CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<HrSettingsUpdatedAuditEvent>(auditEvt);
		Assert.Equal(actorUserId, auditEvent.ActorUserId);
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

		// First update succeeds and bumps Version from 1 to 3 (UpdateHrPolicy and
		// UpdateAssetNumberSettings each increment the shared Version counter once).
		var firstHandler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(),
			new NoOpEmployeeRenumberingService(),
			new FakeCurrentUser(null));

		var firstResult = await firstHandler.HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);
		Assert.True(firstResult.IsSuccess);
		Assert.Equal(5, firstResult.Value!.Version);

		// Second attempt is submitted against the stale Version = 1 read before the first update.
		var auditPublisher = new CapturingAuditEventPublisher();
		var secondHandler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc)),
			auditPublisher,
			new NoOpEmployeeRenumberingService(),
			new FakeCurrentUser(null));

		var secondResult = await secondHandler.HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);

		Assert.True(secondResult.IsFailure);
		Assert.Equal("conflict", secondResult.Error.Code);
		Assert.Empty(auditPublisher.Published);
	}

	[Fact]
	public async Task HandleAsync_Persists_ProbationCheckpoints_And_AttendanceAlertThresholds()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				ProbationCheckpointDay1 = 14,
				ProbationCheckpointDay2 = 45,
				ProbationCheckpointDay3 = null,
				FrequentAbsenceCountThreshold = 6,
				FrequentAbsenceWindowDays = 180,
				LongAbsenceDayThreshold = 21,
				WeekdayPatternOccurrenceThreshold = 2,
				WeekdayPatternWindowDays = 200,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(14, result.Value!.ProbationCheckpointDay1);
		Assert.Equal(45, result.Value.ProbationCheckpointDay2);
		Assert.Null(result.Value.ProbationCheckpointDay3);
		Assert.Equal(6, result.Value.FrequentAbsenceCountThreshold);
		Assert.Equal(180, result.Value.FrequentAbsenceWindowDays);
		Assert.Equal(21, result.Value.LongAbsenceDayThreshold);
		Assert.Equal(2, result.Value.WeekdayPatternOccurrenceThreshold);
		Assert.Equal(200, result.Value.WeekdayPatternWindowDays);

		var savedSettings = await context.CompanySettings.SingleAsync();
		Assert.Equal(14, savedSettings.ProbationCheckpointDay1);
		Assert.Equal(45, savedSettings.ProbationCheckpointDay2);
		Assert.Null(savedSettings.ProbationCheckpointDay3);
		Assert.Equal(6, savedSettings.FrequentAbsenceCountThreshold);
		Assert.Equal(180, savedSettings.FrequentAbsenceWindowDays);
		Assert.Equal(21, savedSettings.LongAbsenceDayThreshold);
		Assert.Equal(2, savedSettings.WeekdayPatternOccurrenceThreshold);
		Assert.Equal(200, savedSettings.WeekdayPatternWindowDays);
	}

	[Fact]
	public async Task HandleAsync_Includes_ProbationCheckpoints_And_AttendanceAlertThresholds_In_AuditEvent_BeforeAndAfter_Snapshots()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);

		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var auditPublisher = new CapturingAuditEventPublisher();
		var updateTime = new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc);
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpEmployeeRenumberingService(), new FakeCurrentUser(null));

		await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				ProbationCheckpointDay1 = 14,
				ProbationCheckpointDay2 = 45,
				ProbationCheckpointDay3 = null,
				FrequentAbsenceCountThreshold = 6,
				FrequentAbsenceWindowDays = 180,
				LongAbsenceDayThreshold = 21,
				WeekdayPatternOccurrenceThreshold = 2,
				WeekdayPatternWindowDays = 200,
			},
			CancellationToken.None);

		var auditEvt = Assert.Single(auditPublisher.Published);
		var auditEvent = Assert.IsType<HrSettingsUpdatedAuditEvent>(auditEvt);

		Assert.NotNull(auditEvent.PreviousSettings);
		// Defaults established by CompanySettings.CreateDefault.
		Assert.Equal(30, auditEvent.PreviousSettings!.ProbationCheckpointDay1);
		Assert.Equal(60, auditEvent.PreviousSettings.ProbationCheckpointDay2);
		Assert.Equal(90, auditEvent.PreviousSettings.ProbationCheckpointDay3);
		Assert.Equal(4, auditEvent.PreviousSettings.FrequentAbsenceCountThreshold);
		Assert.Equal(365, auditEvent.PreviousSettings.FrequentAbsenceWindowDays);
		Assert.Equal(28, auditEvent.PreviousSettings.LongAbsenceDayThreshold);
		Assert.Equal(3, auditEvent.PreviousSettings.WeekdayPatternOccurrenceThreshold);
		Assert.Equal(365, auditEvent.PreviousSettings.WeekdayPatternWindowDays);

		Assert.Equal(14, auditEvent.CurrentSettings.ProbationCheckpointDay1);
		Assert.Equal(45, auditEvent.CurrentSettings.ProbationCheckpointDay2);
		Assert.Null(auditEvent.CurrentSettings.ProbationCheckpointDay3);
		Assert.Equal(6, auditEvent.CurrentSettings.FrequentAbsenceCountThreshold);
		Assert.Equal(180, auditEvent.CurrentSettings.FrequentAbsenceWindowDays);
		Assert.Equal(21, auditEvent.CurrentSettings.LongAbsenceDayThreshold);
		Assert.Equal(2, auditEvent.CurrentSettings.WeekdayPatternOccurrenceThreshold);
		Assert.Equal(200, auditEvent.CurrentSettings.WeekdayPatternWindowDays);
	}

	private static CompaniesDbContext BuildContext()
	{
		var options = new DbContextOptionsBuilder<CompaniesDbContext>()
			.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
			.Options;

		return new CompaniesDbContext(options);
	}
}
