using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Leave.Tests.Jobs;

public class LeaveYearRolloverServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2027, 1, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static LeaveDbContext BuildContext()
    {
        // Rollover wraps its save in an explicit transaction; InMemory provider doesn't support
        // transactions and raises a warning-as-error for it by default (same accommodation used
        // by AdjustLeaveBalanceHandlerTests — a test-provider limitation only).
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LeaveDbContext(options);
    }

    private static LeaveYearRolloverService BuildService(
        LeaveDbContext context, IAuditEventPublisher? auditPublisher = null, ICompanyLeaveSettingsReader? leaveSettingsReader = null) =>
        new(context, new FakeClock(FixedUtcNow), leaveSettingsReader ?? new FakeCompanyLeaveSettingsReader(), auditPublisher ?? new NoOpAuditEventPublisher());

    private static LeaveType CreateLeaveType(
        Guid companyId,
        int defaultEntitlementDays = 25,
        LeaveTypeBehaviour behaviour = LeaveTypeBehaviour.Standard,
        bool isActive = true,
        bool hasBalance = true)
    {
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", defaultEntitlementDays,
            AccrualMethod.Monthly, behaviour, Now, hasBalance);
        if (!isActive)
            leaveType.Deactivate(Now);
        return leaveType;
    }

    private static LeavePolicy CreatePolicy(Guid companyId, int carryOverDays) =>
        LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, carryOverDays, false, false, Now);

    // An active EmployeeLeavePolicyAssignment is now required for rollover eligibility (see
    // LeaveYearRolloverService — "no active assignment" is itself the signal that an employee has
    // left and should not be rolled over). Real usage always creates a LeaveBalance alongside an
    // assignment (EmployeeCreatedHandler / AssignLeavePolicyToEmployeeHandler), so tests that only
    // set up a LeaveBalance also need this to represent a currently-active employee.
    private static EmployeeLeavePolicyAssignment CreateAssignment(Guid companyId, Guid employeeId, Guid leavePolicyId) =>
        EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, leavePolicyId, new DateOnly(2026, 1, 1), Now);

    private static LeaveBalance CreateBalance(
        Guid companyId, Guid employeeId, Guid leaveTypeId, Guid policyId, int policyYear,
        decimal entitlementDays, decimal usedDays = 0, decimal adjustmentDays = 0)
    {
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId, policyYear, entitlementDays,
            new DateOnly(policyYear, 1, 1), Now);
        if (usedDays != 0)
            balance.RecordUsage(usedDays, Now);
        if (adjustmentDays != 0)
            balance.Adjust(adjustmentDays, Now);
        return balance;
    }

    [Fact]
    public async Task RolloverCompanyAsync_Creates_New_Balance_With_Default_Entitlement_When_No_Carry_Over()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 5);
        // Fully used — remaining is exactly zero, so this scenario is a genuine "no carry-over"
        // case (the CarryOverDays=5 limit still allows carry-over, but there is nothing left to
        // carry). The zero-limit and exhausted/negative-remaining cases are covered separately.
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 25m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(1, result.BalancesCreated);
        Assert.Equal(0, result.CarryOverAdjustmentsCreated);

        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(25m, newBalance.EntitlementDays);
        Assert.Equal(0m, newBalance.AdjustmentDays);
        Assert.Equal(employeeId, newBalance.EmployeeId);
        Assert.Equal(leaveType.Id, newBalance.LeaveTypeId);
        Assert.Equal(policy.Id, newBalance.LeavePolicyId);
        Assert.Empty(context.LeaveBalanceAdjustments);
        // A continuing employee's new policy-year balance accrues (for Monthly/Fortnightly leave
        // types) from the new policy year's own start date - see LeaveYearRolloverService (LEAVE-04).
        Assert.Equal(new DateOnly(2027, 1, 1), newBalance.AccrualStartDate);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Uses_Zero_Base_Entitlement_For_Toil_Behaviour_Leave_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25, behaviour: LeaveTypeBehaviour.Toil);
        var policy = CreatePolicy(companyId, carryOverDays: 0);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 10m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(0m, newBalance.EntitlementDays);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Carries_Over_Remaining_Days_Up_To_Policy_Limit()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 5);
        // Remaining = 25 - 10 used = 15, but CarryOverDays limit is 5.
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var service = BuildService(context, auditPublisher);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(1, result.BalancesCreated);
        Assert.Equal(1, result.CarryOverAdjustmentsCreated);

        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(25m, newBalance.EntitlementDays);
        Assert.Equal(5m, newBalance.AdjustmentDays); // capped at policy's CarryOverDays
        Assert.Equal(30m, newBalance.RemainingDays);

        var adjustment = await context.LeaveBalanceAdjustments.SingleAsync();
        Assert.Equal(companyId, adjustment.CompanyId);
        Assert.Equal(employeeId, adjustment.EmployeeId);
        Assert.Equal(leaveType.Id, adjustment.LeaveTypeId);
        Assert.Equal(5m, adjustment.AdjustmentDays);
        Assert.Equal(LeaveBalanceAdjustmentReason.CarryOver, adjustment.Reason);
        Assert.Equal("From policy year 2026", adjustment.Comments);
        Assert.Equal(Guid.Empty, adjustment.AdjustedByEmployeeId);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<LeaveBalanceAdjustedAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(leaveType.Id, auditEvent.LeaveTypeId);
        Assert.Equal(newBalance.Id, auditEvent.LeaveBalanceId);
        Assert.Equal(2027, auditEvent.PolicyYear);
        Assert.Equal(5m, auditEvent.AdjustmentDays);
        Assert.Equal(30m, auditEvent.NewRemainingDays);
        Assert.Equal(Guid.Empty, auditEvent.AdjustedByEmployeeId);
        Assert.Equal("CarryOver", auditEvent.Reason);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Carries_Full_Remaining_When_Below_Policy_Limit()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 10);
        // Remaining = 25 - 22 used = 3, which is below the CarryOverDays limit of 10.
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 22m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(3m, newBalance.AdjustmentDays);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Carries_Nothing_When_Policy_CarryOverDays_Is_Zero_Even_If_Remaining_Is_Positive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 0);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 5m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var service = BuildService(context, auditPublisher);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(1, result.BalancesCreated);
        Assert.Equal(0, result.CarryOverAdjustmentsCreated);

        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(0m, newBalance.AdjustmentDays);
        Assert.Empty(context.LeaveBalanceAdjustments);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Carries_Nothing_When_Remaining_Balance_Is_Exactly_Zero()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 10);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 25m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(0m, newBalance.AdjustmentDays);
        Assert.Empty(context.LeaveBalanceAdjustments);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Carries_Nothing_When_Remaining_Balance_Is_Negative()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 10);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 30m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(0m, newBalance.AdjustmentDays);
        Assert.Empty(context.LeaveBalanceAdjustments);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Skips_Deactivated_Leave_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, isActive: false);
        var policy = CreatePolicy(companyId, carryOverDays: 5);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(LeaveYearRolloverResult.Empty, result);
        Assert.False(await context.LeaveBalances.AnyAsync(b => b.PolicyYear == 2027));
    }

    [Fact]
    public async Task RolloverCompanyAsync_Skips_Leave_Type_With_HasBalance_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, hasBalance: false);
        var policy = CreatePolicy(companyId, carryOverDays: 5);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(LeaveYearRolloverResult.Empty, result);
        Assert.False(await context.LeaveBalances.AnyAsync(b => b.PolicyYear == 2027));
    }

    [Fact]
    public async Task RolloverCompanyAsync_Returns_Empty_And_Writes_Nothing_When_No_Previous_Year_Balances_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var service = BuildService(context);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(LeaveYearRolloverResult.Empty, result);
        Assert.Empty(context.LeaveBalances);
        Assert.Empty(context.LeaveBalanceAdjustments);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Uses_Current_Policy_Assignment_CarryOverDays_When_Employee_Was_Reassigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var oldPolicy = CreatePolicy(companyId, carryOverDays: 2); // previous balance's policy
        var newPolicy = CreatePolicy(companyId, carryOverDays: 20); // employee's current assignment

        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, oldPolicy.Id, 2026, 25m, usedDays: 15m);
        // Remaining = 10, would be capped to 2 under the old policy but should use the new policy's
        // 20-day limit since the employee has since been reassigned.
        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, newPolicy.Id, new DateOnly(2027, 1, 1), Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.AddRange(oldPolicy, newPolicy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(newPolicy.Id, newBalance.LeavePolicyId);
        Assert.Equal(10m, newBalance.AdjustmentDays); // full remaining, under the new policy's 20-day limit
    }

    [Fact]
    public async Task RolloverCompanyAsync_Skips_Employee_When_No_Active_Assignment_Exists()
    {
        // Superseded behaviour: this used to fall back to the previous balance's own policy when no
        // current assignment existed. Now "no active assignment" (never assigned, or deactivated
        // because the employee's departure was finalised) is itself the eligibility signal — such
        // employees are skipped entirely rather than defaulted to their old policy.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 5);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        // No EmployeeLeavePolicyAssignment row for this employee.
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(LeaveYearRolloverResult.Empty, result);
        Assert.False(await context.LeaveBalances.AnyAsync(b => b.PolicyYear == 2027));
    }

    [Fact]
    public async Task RolloverCompanyAsync_Skips_Terminated_Employee_With_Deactivated_Assignment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var terminatedEmployeeId = Guid.NewGuid();
        var activeEmployeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 5);

        var terminatedBalance = CreateBalance(companyId, terminatedEmployeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);
        var activeBalance = CreateBalance(companyId, activeEmployeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);

        var terminatedAssignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, terminatedEmployeeId, policy.Id, new DateOnly(2026, 1, 1), Now);
        terminatedAssignment.Deactivate(Now);

        var activeAssignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, activeEmployeeId, policy.Id, new DateOnly(2026, 1, 1), Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.AddRange(terminatedBalance, activeBalance);
        context.EmployeeLeavePolicyAssignments.AddRange(terminatedAssignment, activeAssignment);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(1, result.BalancesCreated);
        Assert.False(await context.LeaveBalances.AnyAsync(b => b.PolicyYear == 2027 && b.EmployeeId == terminatedEmployeeId));
        Assert.True(await context.LeaveBalances.AnyAsync(b => b.PolicyYear == 2027 && b.EmployeeId == activeEmployeeId));
    }

    [Fact]
    public async Task RolloverCompanyAsync_Does_Not_Alter_Existing_NewYear_Balance_When_Assignment_Deactivated_After_Rollover_Already_Ran()
    {
        // The employee had already rolled over into 2027 (still active at that point) — their
        // departure is only finalised afterwards. A subsequent rerun of RolloverCompanyAsync for
        // 2027 must be a no-op for them: the idempotency guard (existing new-year balance already
        // present) is checked before the active-assignment check, so their existing balance is left
        // untouched rather than retroactively removed or altered.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 5);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);
        var existingNewYearBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2027, 25m);

        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, policy.Id, new DateOnly(2026, 1, 1), Now);
        // Deactivated after the 2027 balance already exists.
        assignment.Deactivate(Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.AddRange(previousBalance, existingNewYearBalance);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(LeaveYearRolloverResult.Empty, result);
        var newBalance = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2027);
        Assert.Equal(existingNewYearBalance.Id, newBalance.Id);
        Assert.Equal(0m, newBalance.AdjustmentDays);
        Assert.Equal(25m, newBalance.EntitlementDays);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Rerun_After_Termination_Remains_Idempotent_For_Both_Active_And_Terminated_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var terminatedEmployeeId = Guid.NewGuid();
        var activeEmployeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 5);

        var terminatedBalance = CreateBalance(companyId, terminatedEmployeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);
        var activeBalance = CreateBalance(companyId, activeEmployeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);

        var terminatedAssignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, terminatedEmployeeId, policy.Id, new DateOnly(2026, 1, 1), Now);
        var activeAssignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, activeEmployeeId, policy.Id, new DateOnly(2026, 1, 1), Now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.AddRange(terminatedBalance, activeBalance);
        context.EmployeeLeavePolicyAssignments.AddRange(terminatedAssignment, activeAssignment);
        await context.SaveChangesAsync();

        var service = BuildService(context);

        // First run: both employees still active — both get a new-year balance.
        var firstRun = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);
        Assert.Equal(2, firstRun.BalancesCreated);

        // Employee is terminated between the two runs.
        terminatedAssignment.Deactivate(Now.AddDays(1));
        await context.SaveChangesAsync();

        var secondRun = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(LeaveYearRolloverResult.Empty, secondRun);
        Assert.Single(await context.LeaveBalances.Where(b => b.PolicyYear == 2027).ToListAsync(),
            b => b.EmployeeId == activeEmployeeId);
        Assert.Single(await context.LeaveBalances.Where(b => b.PolicyYear == 2027).ToListAsync(),
            b => b.EmployeeId == terminatedEmployeeId);
    }

    [Fact]
    public async Task RolloverCompanyAsync_Is_Idempotent_On_Rerun_For_Same_Company_And_Policy_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 5);
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var service = BuildService(context, auditPublisher);

        var firstRun = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);
        Assert.Equal(1, firstRun.BalancesCreated);
        Assert.Equal(1, firstRun.CarryOverAdjustmentsCreated);

        var secondRun = await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        Assert.Equal(0, secondRun.BalancesCreated);
        Assert.Equal(0, secondRun.CarryOverAdjustmentsCreated);

        // Still exactly one new-year balance and one adjustment — no duplicates from the rerun.
        Assert.Single(await context.LeaveBalances.Where(b => b.PolicyYear == 2027).ToListAsync());
        Assert.Single(context.LeaveBalanceAdjustments);
        Assert.Single(auditPublisher.Published); // only published once, on the first run
    }

    [Fact]
    public async Task RolloverCompanyAsync_Computes_PreviousPolicyYear_Label_Correctly_For_NonJanuary_LeaveYear()
    {
        // Only the numeric label matters here (bounds/date-gating is the job's responsibility) —
        // confirms newPolicyYear - 1 is used verbatim regardless of the company's start month.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateLeaveType(companyId, defaultEntitlementDays: 25);
        var policy = CreatePolicy(companyId, carryOverDays: 5);
        // Previous policy year for an April-start company that just entered policy year 2027
        // (Apr 2027 - Mar 2028) is 2026 (Apr 2026 - Mar 2027).
        var previousBalance = CreateBalance(companyId, employeeId, leaveType.Id, policy.Id, 2026, 25m, usedDays: 10m);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(previousBalance);
        context.EmployeeLeavePolicyAssignments.Add(CreateAssignment(companyId, employeeId, policy.Id));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RolloverCompanyAsync(companyId, 2027, CancellationToken.None);

        var adjustment = await context.LeaveBalanceAdjustments.SingleAsync();
        Assert.Equal("From policy year 2026", adjustment.Comments);
    }
}
