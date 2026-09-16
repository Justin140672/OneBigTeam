using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

internal static class LeaveProbes
{
    public static async Task Run()
    {
        await Check(false);
        await Check(true);
    }

    private static async Task Check(bool staleRead)
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var company = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var type = Guid.NewGuid();
        var policy = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var seed = new LeaveDbContext(options))
        {
            seed.LeavePolicies.Add(LeavePolicy.Create(policy, company, "No overdraft", null, 0, false, true, now));
            seed.LeaveTypes.Add(LeaveType.Create(type, company, "Annual Leave", "ANNUAL", 0,
                AccrualMethod.None, LeaveTypeBehaviour.Standard, now, hasBalance: true));
            seed.LeaveBalances.Add(LeaveBalance.Create(Guid.NewGuid(), company, employee, type, policy,
                2026, staleRead ? 20 : 5, new DateOnly(2026, 1, 1), now));
            for (var i = 0; i < 2; i++)
                seed.LeaveRequests.Add(LeaveRequest.Create(ids[i], company, employee, type, policy,
                    new DateOnly(2026, 10, 5 + i * 7), LeaveDayPart.FullDay,
                    new DateOnly(2026, 10, 9 + i * 7), LeaveDayPart.FullDay, 5, null, now));
            await seed.SaveChangesAsync();
        }
        await using var first = new LeaveDbContext(options);
        await using var second = new LeaveDbContext(options);
        if (staleRead)
        {
            await first.LeaveBalances.SingleAsync();
            await second.LeaveBalances.SingleAsync();
        }
        for (var i = 0; i < 2; i++)
        {
            var db = i == 0 ? first : second;
            var effects = new LeaveApprovalEffectsService(db, new NoOpNotificationWriter(),
                new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(),
                new NoOpAuditEventPublisher(), new ToilLedgerService(db));
            var handler = new ApproveLeaveRequestHandler(db, new FakeClock(now.UtcDateTime), effects);
            var result = await handler.HandleAsync(new ApproveLeaveRequestRequest
            {
                CompanyId = company, EmployeeId = employee, LeaveRequestId = ids[i],
                ReviewedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);
            if (result.IsFailure) throw new Exception("Approval behavior changed: re-review finding.");
        }
        await using var verify = new LeaveDbContext(options);
        var balance = await verify.LeaveBalances.SingleAsync();
        var approved = await verify.LeaveRequests.Where(r => r.Status == LeaveRequestStatus.Approved).SumAsync(r => r.TotalDays);
        Console.WriteLine($"LEAVE {(staleRead ? "stale snapshots" : "negative forbidden")}: approved={approved}, used={balance.UsedDays}, remaining={balance.RemainingDays}");
        if (staleRead ? balance.UsedDays != 5 : balance.RemainingDays != -5)
            throw new Exception("Leave observation changed: re-review finding.");
    }
}
