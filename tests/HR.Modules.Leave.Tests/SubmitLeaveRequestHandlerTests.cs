using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.SubmitLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
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

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", (int)entitlementDays,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 5, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            FixedUtcNow.Year, entitlementDays, now);

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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());

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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());

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
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());

        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4m, result.Value!.TotalDays);
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 0, allowNegativeBalance: false, false, now);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            FixedUtcNow.Year, 0.4m, now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());
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
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher());

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
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), publisher, new NoOpAuditEventPublisher());
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
        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(), new NoOpIntegrationEventPublisher(), auditPublisher);
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
}

