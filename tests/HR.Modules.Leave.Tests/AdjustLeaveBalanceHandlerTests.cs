using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.AdjustLeaveBalance;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Leave.Tests;

public class AdjustLeaveBalanceHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static LeaveDbContext BuildContext()
    {
        // The handler under test wraps its save in an explicit database transaction (a ticket
        // requirement for atomicity). EF Core's InMemory provider doesn't support transactions
        // and raises a warning-as-error for it by default; ignore that specific warning here
        // since it's purely a test-provider limitation (real Postgres supports transactions).
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LeaveDbContext(options);
    }

    private static LeaveType CreateActiveLeaveType(Guid companyId) =>
        LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);

    private static AdjustLeaveBalanceHandler BuildHandler(
        LeaveDbContext context,
        Dictionary<Guid, string>? employeeNames = null,
        IAuditEventPublisher? auditPublisher = null) =>
        new(
            context,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(),
            new FakeEmployeeNameReader(employeeNames),
            auditPublisher ?? new NoOpAuditEventPublisher());

    private static AdjustLeaveBalanceRequest BuildRequest(
        Guid companyId,
        Guid employeeId,
        Guid leaveTypeId,
        decimal adjustmentHours,
        bool allowNegativeOverride = false,
        LeaveBalanceAdjustmentReason reason = LeaveBalanceAdjustmentReason.Correction,
        string? comments = "Test adjustment") =>
        new(companyId, employeeId, leaveTypeId, adjustmentHours, reason, comments, allowNegativeOverride)
        {
            AdjustedByEmployeeId = Guid.NewGuid()
        };

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeaveType_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = BuildHandler(context, new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, Guid.NewGuid(), adjustmentHours: 7.5m),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateActiveLeaveType(companyId);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        // Employee name reader returns a dict that does NOT contain employeeId.
        var handler = BuildHandler(context, new Dictionary<Guid, string>());

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, leaveType.Id, adjustmentHours: 7.5m),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Contains(employeeId.ToString(), result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Leave_Balance_Exists_For_Policy_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateActiveLeaveType(companyId);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, leaveType.Id, adjustmentHours: 7.5m),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Applies_Positive_Adjustment_And_Persists_Adjustment_Row()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateActiveLeaveType(companyId);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, false, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, new Dictionary<Guid, string> { [employeeId] = "Jane Doe" }, auditPublisher);
        var adjustedBy = Guid.NewGuid();

        var request = BuildRequest(companyId, employeeId, leaveType.Id, adjustmentHours: 15m, comments: "Awarded extra days") with
        {
            AdjustedByEmployeeId = adjustedBy
        };

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(202.5m, result.Value!.NewRemainingHours); // (25 + 2 - 0) days * 7.5 hours/day
        Assert.Equal(15m, result.Value.AdjustmentHours);
        Assert.Equal(adjustedBy, result.Value.AdjustedByEmployeeId);

        var updatedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(2m, updatedBalance.AdjustmentDays);
        Assert.Equal(27m, updatedBalance.RemainingDays);

        var persistedAdjustment = await context.LeaveBalanceAdjustments.SingleAsync();
        Assert.Equal(companyId, persistedAdjustment.CompanyId);
        Assert.Equal(employeeId, persistedAdjustment.EmployeeId);
        Assert.Equal(leaveType.Id, persistedAdjustment.LeaveTypeId);
        Assert.Equal(15m, persistedAdjustment.AdjustmentHours);
        Assert.Equal(LeaveBalanceAdjustmentReason.Correction, persistedAdjustment.Reason);
        Assert.Equal("Awarded extra days", persistedAdjustment.Comments);
        Assert.Equal(adjustedBy, persistedAdjustment.AdjustedByEmployeeId);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<LeaveBalanceAdjustedAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(leaveType.Id, auditEvent.LeaveTypeId);
        Assert.Equal(balance.Id, auditEvent.LeaveBalanceId);
        Assert.Equal(2m, auditEvent.AdjustmentDays);
        Assert.Equal(27m, auditEvent.NewRemainingDays);
        Assert.Equal(15m, auditEvent.AdjustmentHours);
        Assert.Equal("Correction", auditEvent.Reason);
        Assert.Equal(adjustedBy, auditEvent.AdjustedByEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Allows_Negative_Adjustment_Below_Zero_When_Override_Is_True()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateActiveLeaveType(companyId);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, allowNegativeBalance: false, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, 2026, 5m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, leaveType.Id, adjustmentHours: -60m, allowNegativeOverride: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // (5 entitlement - 8 adjustment days - 0 used) = -3 days remaining
        Assert.Equal(-22.5m, result.Value!.NewRemainingHours);

        var updatedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(-3m, updatedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Allows_Negative_Adjustment_Below_Zero_When_Policy_Allows_Negative_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateActiveLeaveType(companyId);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Flexible", null, 5, allowNegativeBalance: true, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, 2026, 5m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, leaveType.Id, adjustmentHours: -60m, allowNegativeOverride: false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var updatedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(-3m, updatedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Negative_Adjustment_Below_Zero_Without_Override_Or_Policy_Allowance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateActiveLeaveType(companyId);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, allowNegativeBalance: false, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, 2026, 5m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, leaveType.Id, adjustmentHours: -60m, allowNegativeOverride: false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(
            "This adjustment would take the balance below zero. Enable the override to allow it.",
            result.Error.Message);

        // Balance must be untouched.
        var unchangedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, unchangedBalance.AdjustmentDays);
        Assert.Empty(context.LeaveBalanceAdjustments);
    }

    [Fact]
    public async Task HandleAsync_Allows_Negative_Adjustment_That_Does_Not_Go_Below_Zero_Without_Override()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateActiveLeaveType(companyId);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, allowNegativeBalance: false, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, leaveType.Id, adjustmentHours: -15m, allowNegativeOverride: false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // (25 entitlement - 2 adjustment days - 0 used) = 23 days remaining, never negative.
        var updatedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(23m, updatedBalance.RemainingDays);
    }
}
