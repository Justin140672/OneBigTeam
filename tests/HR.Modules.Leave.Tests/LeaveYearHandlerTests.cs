using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Features.CancelLeaveRequest;
using HR.Modules.Leave.Features.InitialiseEmployeeLeave;
using HR.Modules.Leave.Features.RejectLeaveRequest;
using HR.Modules.Leave.Features.SubmitLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

/// <summary>
/// Verifies that all leave handlers use LeaveYearStartMonth when resolving policy years,
/// so companies with non-January leave years (e.g. April) get the correct balance.
/// </summary>
public class LeaveYearHandlerTests
{
    // Employee created in January 2027; company leave year starts in April.
    // GetPolicyYear(Jan 2027, startMonth=4) = 2026 — still in the 2026 leave year.
    private static readonly DateTime JanuaryClockUtc = new(2027, 1, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset JanuaryNow = new(JanuaryClockUtc, TimeSpan.Zero);

    private static readonly FakeCompanyLeaveSettingsReader AprilStartSettings =
        new(CompanyLeaveSettings.Default with { LeaveYearStartMonth = 4 });

    // Leave request dates: 19-21 Jan 2027 (Tue–Thu) — in policy year 2026 under April start
    private static readonly DateOnly LeaveStartDate = new(2027, 1, 19);
    private static readonly DateOnly LeaveEndDate = new(2027, 1, 21);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    // ── SubmitLeaveRequest ──────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_Finds_Policy_Year_2026_Balance_When_Leave_Year_Starts_In_April_And_Date_Is_January_2027()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, JanuaryNow);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Policy", null, 0, allowNegativeBalance: false, JanuaryNow);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            new DateOnly(2026, 4, 1), JanuaryNow);
        // Balance in policy year 2026 — the correct year for Jan 2027 with April start
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            2026, 25m, JanuaryNow);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(JanuaryClockUtc),
            new FakeWorkingPatternProvider(), AprilStartSettings, new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(new SubmitLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveType.Id,
            StartDate = LeaveStartDate,
            StartPart = LeaveDayPart.FullDay,
            EndDate = LeaveEndDate,
            EndPart = LeaveDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Submit_Fails_Balance_Check_When_Balance_Is_In_Wrong_Policy_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, JanuaryNow);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Policy", null, 0, allowNegativeBalance: false, JanuaryNow);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            new DateOnly(2026, 4, 1), JanuaryNow);
        // Balance incorrectly stored in calendar year 2027 — handler should look in 2026 and not find it
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            2027, 25m, JanuaryNow);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new SubmitLeaveRequestHandler(context, new FakeClock(JanuaryClockUtc),
            new FakeWorkingPatternProvider(), AprilStartSettings, new NoOpIntegrationEventPublisher());

        var result = await handler.HandleAsync(new SubmitLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveType.Id,
            StartDate = LeaveStartDate,
            StartPart = LeaveDayPart.FullDay,
            EndDate = LeaveEndDate,
            EndPart = LeaveDayPart.FullDay
        }, CancellationToken.None);

        // Balance in year 2027 not found; 0 remaining → insufficient
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    // ── ApproveLeaveRequest ─────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_Deducts_Policy_Year_2026_Balance_When_Leave_Year_Starts_In_April_And_Start_Date_Is_January_2027()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            LeaveStartDate, LeaveDayPart.FullDay, LeaveEndDate, LeaveDayPart.FullDay,
            3m, "Test", JanuaryNow);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            2026, 25m, JanuaryNow);

        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(JanuaryClockUtc),
            new NoOpIntegrationEventPublisher(), AprilStartSettings);

        var result = await handler.HandleAsync(new ApproveLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = leaveRequest.Id,
            ReviewedByEmployeeId = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(3m, savedBalance.UsedDays);
        Assert.Equal(22m, savedBalance.RemainingDays);
    }

    // ── CancelLeaveRequest ──────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_Restores_Policy_Year_2026_Balance_When_Leave_Year_Starts_In_April_And_Start_Date_Is_January_2027()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            LeaveStartDate, LeaveDayPart.FullDay, LeaveEndDate, LeaveDayPart.FullDay,
            3m, "Test", JanuaryNow);
        leaveRequest.Approve(Guid.NewGuid(), JanuaryNow);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            2026, 25m, JanuaryNow);
        balance.RecordUsage(3m, JanuaryNow);

        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(JanuaryClockUtc), AprilStartSettings);

        var result = await handler.HandleAsync(new CancelLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = leaveRequest.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(25m, savedBalance.RemainingDays);
    }

    // ── RejectLeaveRequest ──────────────────────────────────────────────────────

    [Fact]
    public async Task Reject_Restores_Policy_Year_2026_Balance_When_Leave_Year_Starts_In_April_And_Start_Date_Is_January_2027()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            LeaveStartDate, LeaveDayPart.FullDay, LeaveEndDate, LeaveDayPart.FullDay,
            3m, "Test", JanuaryNow);
        leaveRequest.Approve(Guid.NewGuid(), JanuaryNow);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            2026, 25m, JanuaryNow);
        balance.RecordUsage(3m, JanuaryNow);

        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(JanuaryClockUtc),
            new NoOpIntegrationEventPublisher(), AprilStartSettings);

        var result = await handler.HandleAsync(new RejectLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = leaveRequest.Id,
            ReviewedByEmployeeId = Guid.NewGuid(),
            RejectionReason = "Rejected in error"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(25m, savedBalance.RemainingDays);
    }

    // ── EmployeeCreatedHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task EmployeeCreated_Initialises_Balance_In_Policy_Year_2026_When_Leave_Year_Starts_In_April_And_Created_In_January_2027()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, JanuaryNow);
        context.LeaveTypes.Add(leaveType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                new DateOnly(2026, 4, 1), JanuaryNow));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(JanuaryClockUtc), AprilStartSettings);
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId), CancellationToken.None);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(2026, balance.PolicyYear);
        Assert.Equal(25m, balance.EntitlementDays);
    }
}
