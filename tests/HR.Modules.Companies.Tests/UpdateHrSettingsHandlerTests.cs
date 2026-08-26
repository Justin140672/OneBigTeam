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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
			new NoOpBackgroundJobClient(),
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
			new NoOpBackgroundJobClient(),
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
			new NoOpBackgroundJobClient(),
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
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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
		var handler = new UpdateHrSettingsHandler(context, new FakeClock(updateTime), auditPublisher, new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

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

	// --- SET-08: durable/recoverable employee-renumber side effect scenarios --------------------

	[Fact]
	public async Task HandleAsync_FormatChange_While_Staying_Automatic_Creates_Pending_Outbox_Message_And_Enqueues_Job()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		// CreateDefault seeds EmployeeNumberMode=Automatic, Prefix=null, MinimumLength=4.
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var jobClient = new NoOpBackgroundJobClient();
		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), jobClient, new FakeCurrentUser(null));

		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 6,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);

		var outboxMessage = Assert.Single(context.OutboxMessages);
		Assert.Equal(UpdateHrSettingsHandler.EmployeeRenumberEventType, outboxMessage.EventType);
		Assert.Equal(OutboxMessage.StatusPending, outboxMessage.Status);
		Assert.Equal(company.Id, outboxMessage.CompanyId);

		Assert.Equal("EmployeeRenumberSideEffectJob", Assert.Single(jobClient.EnqueuedJobTypes));

		Assert.Equal(outboxMessage.Id, result.Value!.EmployeeRenumberSideEffectId);
		Assert.Equal("pending", result.Value.EmployeeRenumberSideEffectStatus);
	}

	[Fact]
	public async Task HandleAsync_NonFormatChange_Does_Not_Create_Outbox_Message_Or_Enqueue_Job()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var jobClient = new NoOpBackgroundJobClient();
		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), jobClient, new FakeCurrentUser(null));

		// Stay Automatic, keep prefix/minimum-length exactly as CreateDefault's — only WorkingDays
		// changes, so no format change is detected at all.
		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = null,
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 4,
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Empty(context.OutboxMessages);
		Assert.Empty(jobClient.EnqueuedJobTypes);
		Assert.Null(result.Value!.EmployeeRenumberSideEffectId);
		Assert.Null(result.Value.EmployeeRenumberSideEffectStatus);
	}

	[Fact]
	public async Task HandleAsync_FormatChange_While_Switching_Automatic_To_Manual_Does_Not_Trigger_Renumber()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		// CreateDefault: Automatic, prefix null, minlength 4.
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var jobClient = new NoOpBackgroundJobClient();
		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), jobClient, new FakeCurrentUser(null));

		// Format changes (prefix + minlength) AND mode switches Automatic -> Manual.
		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				EmployeeNumberMode = EmployeeNumberMode.Manual,
				EmployeeNumberPrefix = "MAN-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 8,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Empty(context.OutboxMessages);
		Assert.Empty(jobClient.EnqueuedJobTypes);
		Assert.Null(result.Value!.EmployeeRenumberSideEffectId);
	}

	[Fact]
	public async Task HandleAsync_FormatChange_While_Switching_Manual_To_Automatic_Does_Not_Trigger_Renumber()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		var settings = CompanySettings.CreateDefault(company.Id, now);
		// Manually flip to Manual first so the "before" mode is Manual for this scenario.
		settings.UpdateHrPolicy(
			WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday,
			7.5m, 1, 25, 6, true, false, false, 7, 1, "", 3, NoticePeriodUnit.Months, 1, true,
			EmployeeNumberMode.Manual, null, 1, 4, now);
		company.SetSettings(settings, now);
		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var jobClient = new NoOpBackgroundJobClient();
		var handler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), jobClient, new FakeCurrentUser(null));

		var currentVersion = (await context.CompanySettings.SingleAsync()).Version;

		// Format changes AND mode switches Manual -> Automatic.
		var result = await handler.HandleAsync(
			ValidRequest(company.Id) with
			{
				Version = currentVersion,
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 6,
			},
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Empty(context.OutboxMessages);
		Assert.Empty(jobClient.EnqueuedJobTypes);
		Assert.Null(result.Value!.EmployeeRenumberSideEffectId);
	}

	[Fact]
	public async Task HandleAsync_Returns_Conflict_When_Renumber_Already_InFlight_And_Does_Not_Change_Settings_Or_Create_Second_Outbox_Message()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var firstJobClient = new NoOpBackgroundJobClient();
		var firstHandler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), firstJobClient, new FakeCurrentUser(null));

		var firstResult = await firstHandler.HandleAsync(
			ValidRequest(company.Id) with
			{
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 6,
			},
			CancellationToken.None);
		Assert.True(firstResult.IsSuccess);
		Assert.Single(context.OutboxMessages);

		var secondJobClient = new NoOpBackgroundJobClient();
		var secondHandler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), secondJobClient, new FakeCurrentUser(null));

		var secondResult = await secondHandler.HandleAsync(
			ValidRequest(company.Id) with
			{
				Version = firstResult.Value!.Version,
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP2-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 9,
			},
			CancellationToken.None);

		Assert.True(secondResult.IsFailure);
		Assert.Equal("conflict", secondResult.Error.Code);
		Assert.Empty(secondJobClient.EnqueuedJobTypes);

		// Only the one outbox row from the first call exists — the settings and the outbox table
		// were both left untouched by the rejected second call.
		Assert.Single(context.OutboxMessages);

		// The rejected second call mutated the tracked CompanySettings instance in memory (via
		// UpdateHrPolicy) before returning Conflict without saving — reload from the store (as a
		// fresh request's own DbContext would see it) rather than reading the same in-memory
		// instance, which would otherwise still reflect the unsaved in-memory mutation.
		var savedSettings = await context.CompanySettings.SingleAsync();
		await context.Entry(savedSettings).ReloadAsync();
		Assert.Equal("EMP-", savedSettings.EmployeeNumberPrefix);
		Assert.Equal(6, savedSettings.EmployeeNumberMinimumLength);
		Assert.Equal(firstResult.Value.Version, savedSettings.Version);
	}

	[Fact]
	public async Task HandleAsync_Allows_Unrelated_Settings_Update_While_Renumber_Is_InFlight()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var firstHandler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

		var firstResult = await firstHandler.HandleAsync(
			ValidRequest(company.Id) with
			{
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 6,
			},
			CancellationToken.None);
		Assert.True(firstResult.IsSuccess);
		Assert.Single(context.OutboxMessages);

		var secondHandler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

		// Same employee-number format (no format change) — only an unrelated field (WorkingDays)
		// changes — must succeed even though a renumber for this company is still Pending.
		var secondResult = await secondHandler.HandleAsync(
			ValidRequest(company.Id) with
			{
				Version = firstResult.Value!.Version,
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 6,
				WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday,
			},
			CancellationToken.None);

		Assert.True(secondResult.IsSuccess);
		// Still only the one outbox row from the first call — this update didn't trigger another.
		Assert.Single(context.OutboxMessages);
	}

	[Fact]
	public async Task HandleAsync_Allows_New_FormatChange_Once_Previous_InFlight_Outbox_Message_Is_Processed()
	{
		await using var context = BuildContext();
		var now = new DateTimeOffset(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc));
		var company = Company.Create(Guid.NewGuid(), "Acme", now);
		company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
		context.Companies.Add(company);
		await context.SaveChangesAsync();

		var firstHandler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

		var firstResult = await firstHandler.HandleAsync(
			ValidRequest(company.Id) with
			{
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 6,
			},
			CancellationToken.None);
		Assert.True(firstResult.IsSuccess);

		// Manually transition the in-flight row to Processed, simulating the background job having
		// completed the first renumber.
		var firstOutboxMessage = await context.OutboxMessages.SingleAsync();
		firstOutboxMessage.MarkProcessing(now.AddMinutes(30));
		firstOutboxMessage.MarkProcessed(now.AddMinutes(31));
		await context.SaveChangesAsync();

		var secondHandler = new UpdateHrSettingsHandler(
			context,
			new FakeClock(new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc)),
			new NoOpAuditEventPublisher(), new NoOpBackgroundJobClient(), new FakeCurrentUser(null));

		var secondResult = await secondHandler.HandleAsync(
			ValidRequest(company.Id) with
			{
				Version = firstResult.Value!.Version,
				EmployeeNumberMode = EmployeeNumberMode.Automatic,
				EmployeeNumberPrefix = "EMP2-",
				NextEmployeeNumber = 1,
				EmployeeNumberMinimumLength = 9,
			},
			CancellationToken.None);

		Assert.True(secondResult.IsSuccess);
		Assert.Equal(2, context.OutboxMessages.Count());
		Assert.NotNull(secondResult.Value!.EmployeeRenumberSideEffectId);
		Assert.NotEqual(firstOutboxMessage.Id, secondResult.Value.EmployeeRenumberSideEffectId);
	}
}
