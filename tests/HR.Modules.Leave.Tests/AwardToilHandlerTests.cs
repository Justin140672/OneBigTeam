using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.AwardToil;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class AwardToilHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 13, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static LeaveType CreateToilLeaveType(Guid companyId, DateTimeOffset now) =>
        LeaveType.Create(Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0, AccrualMethod.None, LeaveTypeBehaviour.Toil, now);

    private static EmployeeLeavePolicyAssignment CreateAssignment(Guid companyId, Guid employeeId, DateTimeOffset now) =>
        EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), DateOnly.FromDateTime(now.DateTime), now);

    private static AwardToilRequest BuildRequest(Guid companyId, Guid employeeId, decimal days = 1m, DateOnly? occurredOn = null) =>
        new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            AwardedByEmployeeId = Guid.NewGuid(),
            Days = days,
            OccurredOn = occurredOn ?? new DateOnly(2026, 6, 10),
            Notes = "Worked late on project deadline"
        };

    [Fact]
    public async Task HandleAsync_Creates_Transaction_And_Returns_Response()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = CreateToilLeaveType(companyId, now);
        var assignment = CreateAssignment(companyId, employeeId, now);
        context.LeaveTypes.Add(leaveType);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, days: 0.5m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.5m, result.Value!.Days);
        Assert.Equal(0.5m, result.Value.BalanceRemainingDays);
        Assert.Equal("Worked late on project deadline", result.Value.Notes);

        var transaction = await context.ToilTransactions.SingleAsync();
        Assert.Equal(employeeId, transaction.EmployeeId);
        Assert.Equal(companyId, transaction.CompanyId);
        Assert.Equal(0.5m, transaction.Days);
    }

    [Fact]
    public async Task HandleAsync_Creates_Balance_When_None_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = CreateToilLeaveType(companyId, now);
        var assignment = CreateAssignment(companyId, employeeId, now);
        context.LeaveTypes.Add(leaveType);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, days: 1m), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(leaveType.Id, balance.LeaveTypeId);
        Assert.Equal(1m, balance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Adds_To_Existing_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = CreateToilLeaveType(companyId, now);
        var assignment = CreateAssignment(companyId, employeeId, now);
        var existingBalance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, assignment.LeavePolicyId, 2026, 0m, new DateOnly(2026, 1, 1), now);
        existingBalance.Adjust(2m, now);

        context.LeaveTypes.Add(leaveType);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(existingBalance);
        await context.SaveChangesAsync();

        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, days: 1m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3m, result.Value!.BalanceRemainingDays);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(3m, balance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Toil_Leave_Type()
    {
        await using var context = BuildContext();
        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Policy_Assignment_And_No_Existing_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = CreateToilLeaveType(companyId, now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_Balance_When_One_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = CreateToilLeaveType(companyId, now);
        var assignment = CreateAssignment(companyId, employeeId, now);
        var existingBalance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, assignment.LeavePolicyId, 2026, 0m, new DateOnly(2026, 1, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(existingBalance);
        await context.SaveChangesAsync();

        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        await handler.HandleAsync(BuildRequest(companyId, employeeId, days: 1m), CancellationToken.None);

        Assert.Equal(1, await context.LeaveBalances.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Records_AwardedByEmployeeId_On_Transaction()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var awardedById = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = CreateToilLeaveType(companyId, now);
        var assignment = CreateAssignment(companyId, employeeId, now);
        context.LeaveTypes.Add(leaveType);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new AwardToilRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                AwardedByEmployeeId = awardedById,
                Days = 1m,
                OccurredOn = new DateOnly(2026, 6, 10)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(awardedById, result.Value!.AwardedByEmployeeId);

        var transaction = await context.ToilTransactions.SingleAsync();
        Assert.Equal(awardedById, transaction.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Ignores_Inactive_Toil_Leave_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = CreateToilLeaveType(companyId, now);
        leaveType.Deactivate(now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeaveBalanceAdjusted_And_ToilAwarded_AuditEvents()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var awardedById = Guid.NewGuid();
        var occurredOn = new DateOnly(2026, 6, 10);
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = CreateToilLeaveType(companyId, now);
        var assignment = CreateAssignment(companyId, employeeId, now);
        context.LeaveTypes.Add(leaveType);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var handler = new AwardToilHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), auditPublisher);
        var result = await handler.HandleAsync(
            new AwardToilRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                AwardedByEmployeeId = awardedById,
                Days = 2m,
                OccurredOn = occurredOn,
                Notes = "Overtime on release"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, auditPublisher.Published.Count);

        var balanceAdjusted = Assert.IsType<LeaveBalanceAdjustedAuditEvent>(auditPublisher.Published[0]);
        Assert.Equal(companyId, balanceAdjusted.CompanyId);
        Assert.Equal(employeeId, balanceAdjusted.EmployeeId);
        Assert.Equal(leaveType.Id, balanceAdjusted.LeaveTypeId);
        Assert.Equal(2m, balanceAdjusted.AdjustmentDays);
        Assert.Equal(2m, balanceAdjusted.NewRemainingDays);
        Assert.Equal(awardedById, balanceAdjusted.AdjustedByEmployeeId);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), balanceAdjusted.OccurredAt);

        var toilAwarded = Assert.IsType<ToilAwardedAuditEvent>(auditPublisher.Published[1]);
        Assert.Equal(companyId, toilAwarded.CompanyId);
        Assert.Equal(employeeId, toilAwarded.EmployeeId);
        Assert.Equal(result.Value!.TransactionId, toilAwarded.TransactionId);
        Assert.Equal(awardedById, toilAwarded.AwardedByEmployeeId);
        Assert.Equal(2m, toilAwarded.Days);
        Assert.Equal(occurredOn, toilAwarded.OccurredOn);
        Assert.Equal("Overtime on release", toilAwarded.Notes);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), toilAwarded.OccurredAt);
    }
}
