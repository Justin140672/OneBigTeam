using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.InitialiseEmployeeLeave;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class EmployeeCreatedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Balances_For_All_Active_LeaveTypes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        var inactiveType = LeaveType.Create(Guid.NewGuid(), companyId, "Sick Leave", "SICK", 10,
            AccrualMethod.None, LeaveTypeBehaviour.Sickness, now);
        inactiveType.Deactivate(now);

        context.LeaveTypes.AddRange(activeType, inactiveType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 1), null, new DateOnly(2026, 12, 1)), CancellationToken.None);

        var balances = await context.LeaveBalances.ToListAsync();
        Assert.Single(balances);
        Assert.Equal(employeeId, balances[0].EmployeeId);
        Assert.Equal(activeType.Id, balances[0].LeaveTypeId);
        Assert.Equal(policyId, balances[0].LeavePolicyId);
        // StartDate 2026-06-01 is mid-year on a Jan-Dec leave year: 25 * 214/365 = 14.66 (pro-rated).
        Assert.Equal(14.66m, balances[0].EntitlementDays);
        Assert.Equal(FixedUtcNow.Year, balances[0].PolicyYear);
    }

    [Fact]
    public async Task HandleAsync_Gives_Full_Entitlement_When_StartDate_Is_LeaveYear_Start()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);

        context.LeaveTypes.Add(activeType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 1, 1), null, new DateOnly(2026, 12, 1)),
            CancellationToken.None);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(25, balance.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Gives_Small_ProRated_Entitlement_For_Starter_Near_LeaveYear_End()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);

        context.LeaveTypes.Add(activeType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 12, 27), null, new DateOnly(2026, 12, 1)),
            CancellationToken.None);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0.34m, balance.EntitlementDays); // 25 * 5/365 = 0.34 (rounded)
    }

    [Fact]
    public async Task HandleAsync_ProRates_Correctly_For_NonCalendar_LeaveYear()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var fixedNow = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedNow, TimeSpan.Zero);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);

        context.LeaveTypes.Add(activeType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(fixedNow), now));
        await context.SaveChangesAsync();

        // Company leave year runs April-March (not Jan-Dec).
        var settings = new CompanyLeaveSettings(true, LeaveYearStartMonth: 4, 25, WorkingPattern.Default);
        var handler = new EmployeeCreatedHandler(context, new FakeClock(fixedNow), new FakeCompanyLeaveSettingsReader(settings));
        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 10, 1), null, new DateOnly(2026, 12, 1)),
            CancellationToken.None);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(12.47m, balance.EntitlementDays); // 25 * 182/365 = 12.4657 rounded to 12.47
    }

    [Fact]
    public async Task HandleAsync_ProRates_PartTime_Employees_FullYear_Entitlement_Correctly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        // A part-time leave type reflects a lower full-year entitlement (however that figure was
        // derived from the employee's working pattern) — pro-rating must still apply the same
        // fraction-of-year math on top of it.
        var partTimeAnnualType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 15,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);

        context.LeaveTypes.Add(partTimeAnnualType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 1), null, new DateOnly(2026, 12, 1)),
            CancellationToken.None);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(8.79m, balance.EntitlementDays); // 15 * 214/365 = 8.7945 rounded to 8.79
    }

    [Fact]
    public async Task HandleAsync_Produces_Identical_Entitlement_For_Imported_And_Manually_Created_Employee()
    {
        // Manual creation and import both publish EmployeeCreatedIntegrationEvent and are consumed
        // by the exact same handler — this proves there is no separate calculation path for
        // imports (IsImported only differs in the event payload, entitlement math is untouched).
        await using var manualContext = BuildContext();
        await using var importedContext = BuildContext();
        var companyId = Guid.NewGuid();
        var manualEmployeeId = Guid.NewGuid();
        var importedEmployeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var startDate = new DateOnly(2026, 6, 1);

        foreach (var (context, employeeId) in new[] { (manualContext, manualEmployeeId), (importedContext, importedEmployeeId) })
        {
            var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
                AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
            context.LeaveTypes.Add(activeType);
            context.EmployeeLeavePolicyAssignments.Add(
                EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                    DateOnly.FromDateTime(FixedUtcNow), now));
            await context.SaveChangesAsync();
        }

        var manualHandler = new EmployeeCreatedHandler(manualContext, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await manualHandler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, manualEmployeeId, startDate, null, new DateOnly(2026, 12, 1), IsImported: false),
            CancellationToken.None);

        var importedHandler = new EmployeeCreatedHandler(importedContext, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await importedHandler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, importedEmployeeId, startDate, null, new DateOnly(2026, 12, 1), IsImported: true),
            CancellationToken.None);

        var manualBalance = await manualContext.LeaveBalances.SingleAsync();
        var importedBalance = await importedContext.LeaveBalances.SingleAsync();

        Assert.Equal(manualBalance.EntitlementDays, importedBalance.EntitlementDays);
        Assert.Equal(14.66m, importedBalance.EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_No_Active_LeaveTypes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var inactiveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        inactiveType.Deactivate(now);
        context.LeaveTypes.Add(inactiveType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 1), null, new DateOnly(2026, 12, 1)), CancellationToken.None);

        Assert.Empty(await context.LeaveBalances.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_Employee_Has_No_Policy_Assignment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(activeType);
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 1), null, new DateOnly(2026, 12, 1)), CancellationToken.None);

        Assert.Empty(await context.LeaveBalances.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Auto_Assigns_DefaultLeavePolicy_When_No_Prior_Assignment_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var defaultPolicyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var startDate = new DateOnly(2026, 6, 1);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(activeType);
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, startDate, null, new DateOnly(2026, 12, 1), null, defaultPolicyId),
            CancellationToken.None);

        var assignment = await context.EmployeeLeavePolicyAssignments.SingleAsync();
        Assert.Equal(employeeId, assignment.EmployeeId);
        Assert.Equal(defaultPolicyId, assignment.LeavePolicyId);
        Assert.Equal(startDate, assignment.EffectiveFrom);

        var balances = await context.LeaveBalances.ToListAsync();
        Assert.Single(balances);
        Assert.Equal(defaultPolicyId, balances[0].LeavePolicyId);
        Assert.Equal(14.66m, balances[0].EntitlementDays); // mid-year starter, pro-rated
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Override_Existing_Assignment_With_DefaultLeavePolicy()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var existingPolicyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var activeType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(activeType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, existingPolicyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 1), null, new DateOnly(2026, 12, 1), null, Guid.NewGuid()),
            CancellationToken.None);

        var assignment = await context.EmployeeLeavePolicyAssignments.SingleAsync();
        Assert.Equal(existingPolicyId, assignment.LeavePolicyId);
    }

    [Fact]
    public async Task HandleAsync_Initialises_TOIL_Balance_At_Zero_Regardless_Of_DefaultEntitlementDays()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var toilType = LeaveType.Create(Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 99,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);

        context.LeaveTypes.Add(toilType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 1), null, new DateOnly(2026, 12, 1)), CancellationToken.None);

        var balances = await context.LeaveBalances.ToListAsync();
        Assert.Single(balances);
        Assert.Equal(toilType.Id, balances[0].LeaveTypeId);
        Assert.Equal(0, balances[0].EntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Creates_TOIL_Balance_Alongside_Other_Leave_Types()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var annualType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        var toilType = LeaveType.Create(Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);

        context.LeaveTypes.AddRange(annualType, toilType);
        context.EmployeeLeavePolicyAssignments.Add(
            EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policyId,
                DateOnly.FromDateTime(FixedUtcNow), now));
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 1), null, new DateOnly(2026, 12, 1)), CancellationToken.None);

        var balances = await context.LeaveBalances.ToListAsync();
        Assert.Equal(2, balances.Count);

        var toilBalance = balances.Single(b => b.LeaveTypeId == toilType.Id);
        var annualBalance = balances.Single(b => b.LeaveTypeId == annualType.Id);

        Assert.Equal(0, toilBalance.EntitlementDays);
        Assert.Equal(14.66m, annualBalance.EntitlementDays); // mid-year starter, pro-rated; TOIL always 0
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }
}
