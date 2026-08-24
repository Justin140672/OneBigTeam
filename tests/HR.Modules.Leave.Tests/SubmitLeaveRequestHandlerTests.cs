using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.SubmitLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class SubmitLeaveRequestHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    // 2026-08-03 = Monday, 2026-08-07 = Friday
    private static SubmitLeaveRequestRequest ValidRequest(Guid companyId, Guid employeeId, Guid leaveTypeId) => new()
    {
        CompanyId = companyId,
        EmployeeId = employeeId,
        LeaveTypeId = leaveTypeId,
        StartDate = new DateOnly(2026, 8, 3),
        StartPart = LeaveDayPart.FullDay,
        EndDate = new DateOnly(2026, 8, 7),
        EndPart = LeaveDayPart.FullDay,
        Reason = "Family holiday"
    };

    private static async Task<(LeaveType LeaveType, LeavePolicy Policy, EmployeeLeavePolicyAssignment Assignment, LeaveBalance Balance)>
        SeedStandardSetupAsync(LeaveDbContext context, Guid companyId, Guid employeeId, decimal entitlementDays = 25)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        // AccrualMethod.None: this shared fixture is reused by tests covering conflicts, working
        // days, policy years etc — none of which are exercising accrual pacing itself (that is
        // covered directly by dedicated Monthly/Fortnightly accrual-gating tests below and by
        // LeaveAccrualCalculatorTests), so the full entitlement is available immediately (LEAVE-04).
        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", (int)entitlementDays,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 5, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            FixedUtcNow.Year, entitlementDays, new DateOnly(FixedUtcNow.Year, 1, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        return (leaveType, policy, assignment, balance);
    }

    [Fact]
    public async Task HandleAsync_Creates_LeaveRequest_And_Returns_Response()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, policy, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Pending", result.Value!.Status);
        Assert.Equal(5m, result.Value.TotalDays);
        Assert.Equal(LeaveDayPart.FullDay, result.Value.StartPart);
        Assert.Equal(LeaveDayPart.FullDay, result.Value.EndPart);
        Assert.Equal("Family holiday", result.Value.Reason);
        Assert.Equal(policy.Id, result.Value.LeavePolicyId);
        Assert.Empty(result.Value.Conflicts);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Pending, saved.Status);
        Assert.Equal(5m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeaveType_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeaveType_Is_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        leaveType.Deactivate(now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Employee_Has_No_Policy_Assignment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, Guid.NewGuid(), leaveType.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Insufficient_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // 2 days entitlement, requesting Mon–Fri (5 days)
        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId, entitlementDays: 2);

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Policy_Allows_Negative_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Flexible Policy", null, 0, allowNegativeBalance: true, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Range_Contains_No_Working_Days()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        // 2026-08-08 = Saturday, 2026-08-09 = Sunday
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2026, 8, 8),
                EndDate = new DateOnly(2026, 8, 9)
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_LeaveRequest_For_Saturday_When_Saturday_Is_In_Working_Pattern()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var monToSat = new WorkingPattern(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
            WorkingDays.Thursday | WorkingDays.Friday | WorkingDays.Saturday,
            7.5m);

        var handler = new SubmitLeaveRequestHandler(
            context, new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(monToSat),
            new FakeCompanyLeaveSettingsReader(),
            new FakePublicHolidayReader(),
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(),
            new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));

        // 2026-08-08 = Saturday — a working day in this pattern
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2026, 8, 8),
                EndDate = new DateOnly(2026, 8, 8)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1.0m, result.Value!.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_No_Conflicts_When_No_Overlapping_Requests_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Conflicts);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Pending_Request_Overlaps()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, policy, assignment, _) = await SeedStandardSetupAsync(context, companyId, employeeId, entitlementDays: 25);

        // Existing pending request: Wed–Thu (overlaps with Mon–Fri new request)
        var existing = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 6), LeaveDayPart.FullDay,
            2m, "Existing", now);
        context.LeaveRequests.Add(existing);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Conflicts);
        Assert.Equal(existing.Id, result.Value.Conflicts[0].LeaveRequestId);
        Assert.Equal("Pending", result.Value.Conflicts[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_No_Conflict_For_Adjacent_Non_Overlapping_Request()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, policy, assignment, _) = await SeedStandardSetupAsync(context, companyId, employeeId, entitlementDays: 25);

        // Existing request ends the day before new request starts
        var existing = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            new DateOnly(2026, 7, 27), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 2), LeaveDayPart.FullDay,
            5m, "Prior week", now);
        context.LeaveRequests.Add(existing);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Conflicts);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Flag_Cancelled_Or_Rejected_Requests_As_Conflicts()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, policy, assignment, _) = await SeedStandardSetupAsync(context, companyId, employeeId, entitlementDays: 25);

        var cancelled = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Cancelled", now);
        cancelled.Cancel(now);

        var rejected = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Rejected", now);
        rejected.Reject(Guid.NewGuid(), now);

        context.LeaveRequests.AddRange(cancelled, rejected);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Conflicts);
    }

    [Fact]
    public async Task HandleAsync_Counts_Public_Holiday_As_Working_Day_When_Exclusion_Is_Disabled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        // With exclusion OFF the holiday counts as a working day — still 5 days (reader not consulted)
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false }),
            new FakePublicHolidayReader([new DateOnly(2026, 8, 5)]),
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(),
            new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));

        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value!.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Public_Holiday_From_Working_Days_When_Exclusion_Is_Enabled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        // With exclusion ON the holiday is skipped — 4 days instead of 5
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = true }),
            new FakePublicHolidayReader([new DateOnly(2026, 8, 5)]),
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(),
            new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));

        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4m, result.Value!.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_ExcludedPublicHolidays_Consistent_With_PreviewLeaveRequestHandler()
    {
        // LEAVE-08: SubmitLeaveRequestHandler must surface the same public-holiday-in-range
        // warning PreviewLeaveRequestHandler returns, using the same shared LeaveWarningCalculator.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var holidayReader = new FakePublicHolidayReader([new DateOnly(2026, 8, 5)], "Summer Bank Holiday");

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = true }),
            holidayReader,
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(),
            new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)),
            new LeaveWarningCalculator(holidayReader));

        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var excluded = Assert.Single(result.Value!.ExcludedPublicHolidays);
        Assert.Equal(new DateOnly(2026, 8, 5), excluded.Date);
        Assert.Equal("Summer Bank Holiday", excluded.Name);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Flag_Another_Employees_Request_As_Conflict()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, policy, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId, entitlementDays: 25);

        var otherRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, otherEmployeeId, leaveType.Id, policy.Id,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Other employee", now);
        context.LeaveRequests.Add(otherRequest);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Conflicts);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Deduct_Balance_On_Submit()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, balance) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(25m, savedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Approved_Request_Overlaps()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, policy, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId, entitlementDays: 25);

        var approved = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 6), LeaveDayPart.FullDay,
            2m, "Existing approved", now);
        approved.Approve(Guid.NewGuid(), now);
        context.LeaveRequests.Add(approved);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Conflicts);
        Assert.Equal(approved.Id, result.Value.Conflicts[0].LeaveRequestId);
        Assert.Equal("Approved", result.Value.Conflicts[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Half_Day_Requested_With_Insufficient_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 0, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            FixedUtcNow.Year, 0.4m, new DateOnly(FixedUtcNow.Year, 1, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2026, 8, 3),
                StartPart = LeaveDayPart.Morning,
                EndDate = new DateOnly(2026, 8, 3),
                EndPart = LeaveDayPart.Morning
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Exclude_Other_Company_Public_Holiday()
    {
        await using var context = BuildContext();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyB, employeeId);

        // Company B has no public holidays — reader returns empty
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = true }),
            new FakePublicHolidayReader(),
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(),
            new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));

        var result = await handler.HandleAsync(ValidRequest(companyB, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value!.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeaveRequestedIntegrationEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), publisher, new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var request = ValidRequest(companyId, employeeId, leaveType.Id);
        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(publisher.Published);
        var submitted = Assert.IsType<LeaveRequestedIntegrationEvent>(evt);

        Assert.Equal(companyId, submitted.CompanyId);
        Assert.Equal(employeeId, submitted.EmployeeId);
        Assert.Equal(result.Value!.Id, submitted.LeaveRequestId);
        Assert.Equal(leaveType.Id, submitted.LeaveTypeId);
        Assert.Equal(request.StartDate, submitted.StartDate);
        Assert.Equal(request.EndDate, submitted.EndDate);
        Assert.Equal(result.Value.TotalDays, submitted.TotalDays);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), submitted.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeaveSubmittedAuditEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var auditPublisher = new CapturingAuditEventPublisher();
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), auditPublisher, new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var request = ValidRequest(companyId, employeeId, leaveType.Id);
        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvt = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<LeaveSubmittedAuditEvent>(auditEvt);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(result.Value!.Id, auditEvent.LeaveRequestId);
        Assert.Equal(leaveType.Id, auditEvent.LeaveTypeId);
        Assert.Equal(request.StartDate, auditEvent.StartDate);
        Assert.Equal(request.EndDate, auditEvent.EndDate);
        Assert.Equal(result.Value.TotalDays, auditEvent.TotalDays);
        Assert.Equal("Family holiday", auditEvent.Reason);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), auditEvent.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_Checks_Balance_For_Requests_StartDate_Policy_Year_Not_Todays()
    {
        // Today (FixedUtcNow) is in policy year 2026, but the request's StartDate falls in 2027.
        // Only a 2027 balance row exists with sufficient days — submission must succeed because the
        // policy year is derived from the request's StartDate, not from "today".
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        // AccrualMethod.None: "today" (clock) is in policy year 2026, before this 2027 balance's
        // own accrual start date - Monthly accrual would (correctly) report zero accrued here,
        // which is not what this test is verifying (year resolution, not accrual pacing).
        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 5, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        // AccrualStartDate set before "today" (FixedUtcNow, still in 2026), not the balance's own
        // 2027 policy year start, since AccrualMethod.None still requires asOfDate >= accrualStartDate
        // to clear the gate (see LeaveAccrualCalculator) - not what this test is verifying (policy
        // year resolution, not accrual pacing).
        var balance2027 = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            2027, 25m, new DateOnly(2026, 1, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance2027);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id) with
            {
                // 2027-01-04 = Monday, 2027-01-08 = Friday
                StartDate = new DateOnly(2027, 1, 4),
                EndDate = new DateOnly(2027, 1, 8)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Checks_Future_Years_Balance_Even_When_Current_Years_Balance_Is_Insufficient()
    {
        // Proves the policy year is derived from StartDate, not the clock: the *current* policy
        // year (2026) balance is insufficient, but the *future* year (2027, matching StartDate)
        // has enough days. With the fix this must succeed — the old buggy code checked the 2026
        // balance regardless of the request's actual policy year and would have failed here.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        // AccrualMethod.None: pacing is not the point of this test (see comment above); the full
        // stored entitlement must be available immediately in both policy years.
        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 5, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var balance2026 = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            2026, 1m, new DateOnly(2026, 1, 1), now); // insufficient for the 5-day request
        var balance2027 = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            2027, 25m, new DateOnly(2026, 1, 1), now); // sufficient, and matches the request's StartDate policy year

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.AddRange(balance2026, balance2027);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2027, 1, 4),
                EndDate = new DateOnly(2027, 1, 8)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_With_No_Balance_Rows_When_LeaveType_HasBalance_Is_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Unpaid Leave", "UNPAID", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now, hasBalance: false);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 5, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.LeaveBalances);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Request_Spans_Two_Policy_Years()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2026, 12, 28),
                EndDate = new DateOnly(2027, 1, 4)
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Reject_Cross_Year_Request_For_Toil_Leave_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 5, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(10m, now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2026, 12, 28),
                EndDate = new DateOnly(2027, 1, 4)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Request_Exceeding_Accrued_Balance_Even_Though_Raw_Entitlement_Would_Cover_It()
    {
        // LEAVE-04 wiring: Monthly accrual with an accrual start date of Feb 1 2026 means, by
        // FixedUtcNow (Jun 12 2026), only complete monthly periods Feb1->Mar1->Apr1->May1->Jun1 = 4
        // of the 10 total periods (Feb1..Dec1) in this Jan-Dec policy year have elapsed.
        // Accrued = 24 * 4/10 = 9.60 - comfortably enough to cover a 5-day request against the
        // *raw* 24-day entitlement, but the handler must gate on the accrued figure and reject a
        // 10-day request that exceeds it.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 24,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 0, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            new DateOnly(2026, 2, 1), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            2026, 24m, new DateOnly(2026, 2, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));

        // 2026-08-03 (Mon) - 2026-08-14 (Fri, next week) = 10 working days > 9.60 accrued.
        var result = await handler.HandleAsync(
            ValidRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2026, 8, 3),
                EndDate = new DateOnly(2026, 8, 14)
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("9.6", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Request_Is_Within_Accrued_Balance_Though_Below_Raw_Entitlement()
    {
        // Same accrual setup as above (9.60 accrued of a 24-day raw entitlement) but the request
        // (5 days) fits within what has actually accrued.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 24,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 0, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            new DateOnly(2026, 2, 1), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            2026, 24m, new DateOnly(2026, 2, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));

        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequiresApproval_False_Auto_Approves_Directly_And_Does_Not_Publish_LeaveRequestedIntegrationEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Auto-approve Policy", null, 5,
            allowNegativeBalance: false, isDefault: false, now, requiresApproval: false);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            FixedUtcNow.Year, 25m, new DateOnly(FixedUtcNow.Year, 1, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), publisher, new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), publisher, new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));

        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Approved", result.Value!.Status);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Approved, saved.Status);
        Assert.Equal(employeeId, saved.ReviewedByEmployeeId);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(5m, savedBalance.UsedDays);

        Assert.DoesNotContain(publisher.Published, e => e is LeaveRequestedIntegrationEvent);
        Assert.Contains(publisher.Published, e => e is LeaveApprovedIntegrationEvent);
    }

    [Fact]
    public async Task HandleAsync_RequiresApproval_True_Preserves_Pending_Behaviour()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (leaveType, _, _, _) = await SeedStandardSetupAsync(context, companyId, employeeId);

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), publisher, new NoOpAuditEventPublisher(), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)), new LeaveWarningCalculator(new FakePublicHolidayReader()));

        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Pending", result.Value!.Status);
        Assert.IsType<LeaveRequestedIntegrationEvent>(Assert.Single(publisher.Published));
    }
}

