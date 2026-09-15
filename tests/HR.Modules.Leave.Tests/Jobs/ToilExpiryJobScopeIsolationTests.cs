using HR.Modules.Companies.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Leave.Tests.Jobs;

/// <summary>
/// P1 follow-up (Ticket 4): proves <see cref="ToilExpiryJob"/>'s per-company
/// <c>scopeFactory.CreateAsyncScope()</c> actually isolates one company's failure from the next,
/// rather than just documenting the intent. Builds a real (minimal) DI container - not a hand
/// rolled fake IServiceScopeFactory - so <c>CreateAsyncScope</c> genuinely produces a fresh
/// <see cref="LeaveDbContext"/> (and fresh <see cref="ToilExpiryService"/>, since it is
/// constructor-injected with that DbContext) per company, exactly as in production.
///
/// The row-lock behaviour itself is not exercised here (InMemory is not relational - see
/// ToilExpiryServiceTests' remarks); this file is about proving the *scope* boundary, which is
/// independent of the storage provider.
/// </summary>
public class ToilExpiryJobScopeIsolationTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly AsOf = new(2026, 7, 1);

    /// <summary>
    /// Throws for one designated "faulty" company's audit publish (which happens after
    /// ExpireCompanyAsync's transaction has already committed - see ToilExpiryService), so the
    /// exception surfaces from inside ToilExpiryJob's per-company try/catch exactly like a real
    /// downstream failure would, without needing to corrupt the DbContext itself to provoke it.
    /// </summary>
    private sealed class FaultingForCompanyAuditPublisher(Guid faultyCompanyId) : IAuditEventPublisher
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            if (auditEvent is ToilExpiredAuditEvent e && e.CompanyId == faultyCompanyId)
                throw new InvalidOperationException("Simulated downstream failure for the faulty company.");

            Published.Add(auditEvent!);
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildContainer(string storeName, Guid faultyCompanyId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<LeaveDbContext>(builder =>
        {
            builder.UseInMemoryDatabase(storeName);
            builder.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            builder.UseVersionedAggregates();
        });

        services.AddSingleton<IClock>(new FakeClock(FixedUtcNow));
        services.AddSingleton<ICompanyTimeZoneReader>(new FakeCompanyTimeZoneReader("UTC"));
        services.AddSingleton<IAuditEventPublisher>(new FaultingForCompanyAuditPublisher(faultyCompanyId));
        services.AddSingleton<ILogger<ToilExpiryJob>>(NullLogger<ToilExpiryJob>.Instance);
        services.AddScoped<ToilExpiryService>();
        services.AddScoped<ToilExpiryJob>();

        return services.BuildServiceProvider();
    }

    private static (LeaveType LeaveType, LeaveBalance Balance, ToilTransaction Bucket) SeedDueToilAward(
        Guid companyId, decimal awardedDays)
    {
        var employeeId = Guid.NewGuid();
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, Now, hasBalance: true, toilExpiryDays: 30);
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m,
            new DateOnly(2026, 1, 1), Now);
        balance.Adjust(awardedDays, Now);
        var bucket = ToilTransaction.CreateEarned(
            Guid.NewGuid(), companyId, employeeId, balance.Id, Guid.NewGuid(),
            awardedDays, new DateOnly(2026, 5, 1), AsOf.AddDays(-1), "Overtime", Now);

        return (leaveType, balance, bucket);
    }

    [Fact]
    public async Task One_Companys_Faulted_Iteration_Does_Not_Prevent_The_Next_Companys_Successful_Processing()
    {
        var storeName = "toil-job-isolation-" + Guid.NewGuid().ToString("N");
        var faultyCompanyId = Guid.NewGuid();
        var healthyCompanyId = Guid.NewGuid();

        await using var provider = BuildContainer(storeName, faultyCompanyId);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

            var (faultyType, faultyBalance, faultyBucket) = SeedDueToilAward(faultyCompanyId, 4m);
            var (healthyType, healthyBalance, healthyBucket) = SeedDueToilAward(healthyCompanyId, 5m);

            db.LeaveTypes.AddRange(faultyType, healthyType);
            db.LeaveBalances.AddRange(faultyBalance, healthyBalance);
            db.ToilTransactions.AddRange(faultyBucket, healthyBucket);
            await db.SaveChangesAsync();
        }

        var job = provider.GetRequiredService<ToilExpiryJob>();
        // ToilExpiryJob resolves its own scopes internally via IServiceScopeFactory - not via the
        // job's own DI-injected dependencies here - so this exercises the exact same
        // scope-per-company code path as production.
        await job.ExecuteAsync();

        // Both companies' balances end up correctly expired, even though the faulty company's
        // iteration threw partway through (after its own transaction had already committed, but
        // before the job's per-company try/catch swallowed the exception). Nothing about the
        // faulty company's failed scope leaked into or blocked the healthy company's independent
        // scope - proven by asserting the healthy company's result regardless of which company the
        // job happened to process first (order is not asserted or relied upon).
        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var faultyExpired = await verifyDb.ToilTransactions
            .Where(t => t.CompanyId == faultyCompanyId && t.Type == ToilTransactionType.Expired)
            .ToListAsync();
        var healthyExpired = await verifyDb.ToilTransactions
            .Where(t => t.CompanyId == healthyCompanyId && t.Type == ToilTransactionType.Expired)
            .ToListAsync();

        Assert.Single(faultyExpired); // committed before the simulated downstream fault fired
        Assert.Equal(4m, faultyExpired[0].Days);

        Assert.Single(healthyExpired); // entirely unaffected by the faulty company's exception
        Assert.Equal(5m, healthyExpired[0].Days);

        var faultyFinalBalance = await verifyDb.LeaveBalances.SingleAsync(b => b.CompanyId == faultyCompanyId);
        var healthyFinalBalance = await verifyDb.LeaveBalances.SingleAsync(b => b.CompanyId == healthyCompanyId);
        Assert.Equal(0m, faultyFinalBalance.RemainingDays);
        Assert.Equal(0m, healthyFinalBalance.RemainingDays);
    }

    [Fact]
    public async Task Repeat_Run_After_A_Faulted_Iteration_Is_Still_Idempotent_For_Both_Companies()
    {
        // Guards against a regression where a faulted company's leftover tracked state on a
        // *shared* DbContext could cause a second run to double-expire - the very bug this ticket
        // fixes by giving every iteration (and every job run) a fresh scope.
        var storeName = "toil-job-isolation-repeat-" + Guid.NewGuid().ToString("N");
        var faultyCompanyId = Guid.NewGuid();
        var healthyCompanyId = Guid.NewGuid();

        await using var provider = BuildContainer(storeName, faultyCompanyId);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var (faultyType, faultyBalance, faultyBucket) = SeedDueToilAward(faultyCompanyId, 4m);
            var (healthyType, healthyBalance, healthyBucket) = SeedDueToilAward(healthyCompanyId, 5m);

            db.LeaveTypes.AddRange(faultyType, healthyType);
            db.LeaveBalances.AddRange(faultyBalance, healthyBalance);
            db.ToilTransactions.AddRange(faultyBucket, healthyBucket);
            await db.SaveChangesAsync();
        }

        var job = provider.GetRequiredService<ToilExpiryJob>();
        await job.ExecuteAsync();
        await job.ExecuteAsync(); // Hangfire-style retry/re-run

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        Assert.Equal(1, await verifyDb.ToilTransactions.CountAsync(t => t.CompanyId == faultyCompanyId && t.Type == ToilTransactionType.Expired));
        Assert.Equal(1, await verifyDb.ToilTransactions.CountAsync(t => t.CompanyId == healthyCompanyId && t.Type == ToilTransactionType.Expired));
    }
}
