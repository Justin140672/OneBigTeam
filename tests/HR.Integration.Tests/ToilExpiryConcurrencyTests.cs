using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Integration.Tests;

/// <summary>
/// P1 follow-up (Ticket 4): real-Postgres coverage for <see cref="ToilExpiryService"/>'s new
/// concurrency boundary (row-lock every candidate LeaveBalance up front, inside a transaction,
/// before reading the TOIL ledger - see the class-level doc comment on ToilExpiryService for the
/// full rationale). This only means anything against a real relational database - EF Core's
/// InMemory provider has no concept of row locks (see HR.Modules.Leave.Tests/Jobs/
/// ToilExpiryServiceTests.cs's remarks), so these tests run through
/// <see cref="ApiWebApplicationFactory"/>'s Testcontainers-backed Postgres, resolving a fresh
/// scoped <see cref="LeaveDbContext"/>/<see cref="ToilExpiryService"/> per concurrent caller via
/// <c>factory.Services.CreateScope()</c>, mirroring how <see cref="ToilExpiryJob"/> itself gets a
/// fresh scope per company in production.
/// </summary>
[Collection("Integration")]
public class ToilExpiryConcurrencyTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateOnly AsOf = new(2026, 7, 1);

    public ToilExpiryConcurrencyTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid CompanyId, Guid EmployeeId, Guid BalanceId, Guid BucketId)> SeedDueToilAwardAsync(decimal awardedDays)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now, hasBalance: true, toilExpiryDays: 30);
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m,
            new DateOnly(2026, 1, 1), now);
        balance.Adjust(awardedDays, now);
        var bucket = ToilTransaction.CreateEarned(
            Guid.NewGuid(), companyId, employeeId, balance.Id, Guid.NewGuid(),
            awardedDays, new DateOnly(2026, 5, 1), AsOf.AddDays(-1), "Overtime", now);

        db.LeaveTypes.Add(leaveType);
        db.LeaveBalances.Add(balance);
        db.ToilTransactions.Add(bucket);
        await db.SaveChangesAsync();

        return (companyId, employeeId, balance.Id, bucket.Id);
    }

    [Fact]
    public async Task Two_Concurrent_Expiry_Runs_For_The_Same_Company_Never_Double_Expire_The_Same_Bucket()
    {
        var (companyId, _, _, bucketId) = await SeedDueToilAwardAsync(awardedDays: 6m);

        // Two independent scopes -> two independent LeaveDbContext/ToilExpiryService instances,
        // exactly like two overlapping ToilExpiryJob iterations (or a retried/duplicated Hangfire
        // execution) would get. Task.WhenAll gives Postgres a genuine opportunity to interleave the
        // two SELECT ... FOR UPDATE calls - whichever arrives second blocks until the first's
        // transaction commits, then re-reads fresh ledger state rather than acting on a stale
        // snapshot (see ToilExpiryService's class doc comment).
        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var serviceA = scopeA.ServiceProvider.GetRequiredService<ToilExpiryService>();
        var serviceB = scopeB.ServiceProvider.GetRequiredService<ToilExpiryService>();

        var taskA = serviceA.ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);
        var taskB = serviceB.ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);
        var results = await Task.WhenAll(taskA, taskB);

        // Both calls "succeed" - no exception/crash - but exactly one of them actually created the
        // Expired transaction; the other found the bucket already accounted for (via the in-memory
        // idempotency guard, backstopped by the unique filtered index) and is a safe no-op.
        Assert.Contains(results, r => r.TransactionsCreated == 1);
        Assert.Contains(results, r => r.TransactionsCreated == 0);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var expiredTransactions = await verifyDb.ToilTransactions
            .Where(t => t.RelatedTransactionId == bucketId && t.Type == ToilTransactionType.Expired)
            .ToListAsync();
        Assert.Single(expiredTransactions); // never double-expired
        Assert.Equal(6m, expiredTransactions[0].Days);

        var balance = await verifyDb.LeaveBalances.SingleAsync(b => b.CompanyId == companyId);
        Assert.Equal(0m, balance.RemainingDays); // deducted exactly once, not twice
    }

    [Fact]
    public async Task Expiry_Racing_Concurrent_Toil_Usage_Produces_No_Double_Counting_Regardless_Of_Interleaving()
    {
        var (companyId, employeeId, _, bucketId) = await SeedDueToilAwardAsync(awardedDays: 10m);

        using var expiryScope = _factory.Services.CreateScope();
        var expiryService = expiryScope.ServiceProvider.GetRequiredService<ToilExpiryService>();

        using var consumeScope = _factory.Services.CreateScope();
        var consumeDb = consumeScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var consumeLedger = new ToilLedgerService(consumeDb);

        // ToilLedgerService.ConsumeAsync only stages entity mutations - callers own
        // SaveChangesAsync (see its doc comment) - so this wraps both steps to mirror what
        // ApproveLeaveRequestHandler/LeaveApprovalEffectsService would do for a real leave
        // approval consuming this same TOIL balance, racing the expiry job. A losing consume
        // surfaces as DbUpdateConcurrencyException here exactly as it would in the real handler's
        // own try/catch (SubmitLeaveRequestHandler/SubmitLeaveRequestDraftHandler/
        // ApproveLeaveRequestHandler) - both outcomes are legitimate depending on which writer
        // Postgres let proceed first.
        async Task<bool> ConsumeAsync()
        {
            var consumeResult = await consumeLedger.ConsumeAsync(
                companyId, employeeId, (await consumeDb.LeaveTypes.SingleAsync(lt => lt.CompanyId == companyId)).Id,
                4m, Guid.NewGuid(), Guid.NewGuid(), AsOf.AddDays(-1), allowNegativeBalance: false,
                DateTimeOffset.UtcNow, CancellationToken.None);

            if (consumeResult.IsFailure)
                return false;

            try
            {
                await consumeDb.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        var expiryTask = expiryService.ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);
        var consumeTask = ConsumeAsync();
        await Task.WhenAll(expiryTask, consumeTask);

        var consumeWon = await consumeTask;

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var expired = await verifyDb.ToilTransactions
            .SingleOrDefaultAsync(t => t.RelatedTransactionId == bucketId && t.Type == ToilTransactionType.Expired);
        var used = await verifyDb.ToilTransactions
            .Where(t => t.RelatedTransactionId == bucketId && t.Type == ToilTransactionType.Used)
            .SumAsync(t => (decimal?)t.Days) ?? 0m;

        var balance = await verifyDb.LeaveBalances.SingleAsync(b => b.CompanyId == companyId);

        if (consumeWon)
        {
            // Consume's 4-day usage committed before (or independently of) expiry's read - expiry
            // must only expire the true remainder (10 - 4 = 6), never the full original 10 (which
            // would silently double-count the 4 already used).
            Assert.Equal(4m, used);
            Assert.NotNull(expired);
            Assert.Equal(6m, expired!.Days);
        }
        else
        {
            // Consume lost the race (its stale-versioned save was rejected) - the entire 10-day
            // bucket is legitimately expired since no usage was ever actually persisted against it.
            Assert.Equal(0m, used);
            Assert.NotNull(expired);
            Assert.Equal(10m, expired!.Days);
        }

        // Whichever branch won, the ledger and the aggregate balance stay internally consistent:
        // usage + expiry always accounts for exactly the 10 awarded days - never more (double
        // spend/double expiry), never less (a lost update).
        Assert.Equal(10m, used + expired!.Days);
        Assert.Equal(0m, balance.RemainingDays);
    }

    /// <summary>
    /// P1.1 follow-up: proves the old load-before-lock window is genuinely closed, not just "usually
    /// fine". Pauses <see cref="ToilExpiryService.ExpireCompanyAsync"/> (via
    /// <see cref="ToilExpiryService.TestOnlyAfterLockedLoadAsync"/>) immediately after it has taken
    /// the row lock, then attempts a concurrent TOIL usage against the very same balance row from a
    /// second connection/scope. Because the row lock is held for the rest of expiry's transaction, a
    /// concurrent writer's <c>SaveChangesAsync</c> against that row must genuinely block at the
    /// database level for the whole pause - it cannot sneak in and commit mid-calculation the way it
    /// could before this fix (when the balance was loaded, unlocked, before the lock was taken).
    /// Once expiry resumes and commits, the blocked usage attempt is unblocked, but by then the
    /// bucket has already been correctly expired in full (the usage never got a chance to be
    /// observed by expiry's calculation), so the usage is correctly rejected as insufficient balance
    /// rather than silently double-counted against an already-expired bucket.
    /// </summary>
    [Fact]
    public async Task Concurrent_Toil_Usage_Attempted_During_The_Locked_Read_Window_Blocks_Until_Expiry_Commits_Then_Loses_The_Race()
    {
        var (companyId, employeeId, _, bucketId) = await SeedDueToilAwardAsync(awardedDays: 10m);

        using var expiryScope = _factory.Services.CreateScope();
        var expiryService = expiryScope.ServiceProvider.GetRequiredService<ToilExpiryService>();

        var reachedPause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        expiryService.TestOnlyAfterLockedLoadAsync = async (_, _, _) =>
        {
            reachedPause.TrySetResult();
            await releasePause.Task;
        };

        var expiryTask = expiryService.ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);
        await reachedPause.Task; // expiry now holds the row lock, inside its transaction, and is paused

        using var consumeScope = _factory.Services.CreateScope();
        var consumeDb = consumeScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var consumeLedger = new ToilLedgerService(consumeDb);

        async Task<bool> TryConsumeAsync()
        {
            var leaveTypeId = (await consumeDb.LeaveTypes.SingleAsync(lt => lt.CompanyId == companyId)).Id;
            // occurredOn must be strictly before the bucket's ExpiresOn (seeded one day before AsOf)
            // so ToilLedgerService.GetOpenBucketsOrderedAsync's "bucket.ExpiresOn <= asOf" guard does
            // not itself treat the bucket as already-expired-for-consumption purposes - that check is
            // orthogonal to the row-lock race this test is actually exercising.
            var consumeResult = await consumeLedger.ConsumeAsync(
                companyId, employeeId, leaveTypeId, 4m, Guid.NewGuid(), Guid.NewGuid(), AsOf.AddDays(-2),
                allowNegativeBalance: false, DateTimeOffset.UtcNow, CancellationToken.None);

            if (consumeResult.IsFailure)
                return false;

            try
            {
                await consumeDb.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Consume read the balance/ledger before expiry committed, then blocked on the same
                // row lock; once expiry's commit advances the balance's Version, this stale-versioned
                // save is correctly rejected rather than silently overwriting expiry's result.
                return false;
            }
        }

        var consumeTask = TryConsumeAsync();

        // Give the concurrent usage every opportunity to complete while expiry is still paused
        // mid-transaction. It must not - the row lock has to keep it blocked for the whole window.
        var completedWhilePaused = await Task.WhenAny(consumeTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.NotSame(consumeTask, completedWhilePaused);

        releasePause.TrySetResult();
        var expiryResult = await expiryTask;
        var consumeSucceeded = await consumeTask;

        Assert.Equal(1, expiryResult.TransactionsCreated);
        Assert.False(consumeSucceeded); // lost the race - the bucket had already expired by the time it ran

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var expired = await verifyDb.ToilTransactions
            .SingleAsync(t => t.RelatedTransactionId == bucketId && t.Type == ToilTransactionType.Expired);
        Assert.Equal(10m, expired.Days); // full award - the usage never got to reduce it

        Assert.False(await verifyDb.ToilTransactions.AnyAsync(t => t.RelatedTransactionId == bucketId && t.Type == ToilTransactionType.Used));

        var balance = await verifyDb.LeaveBalances.SingleAsync(b => b.CompanyId == companyId);
        Assert.Equal(0m, balance.RemainingDays);
    }

    /// <summary>
    /// Same load-before-lock window as above, but with a concurrent TOIL usage *reversal* instead of
    /// a fresh usage. A Used transaction is committed against the bucket before expiry starts, so
    /// expiry's own (correct) calculation only expires the true remainder. The reversal of that same
    /// usage is then attempted while expiry is paused holding the row lock - it must also block for
    /// the whole pause, only proceeding once expiry's transaction has committed.
    /// </summary>
    [Fact]
    public async Task Concurrent_Toil_Usage_Reversal_Attempted_During_The_Locked_Read_Window_Blocks_Until_Expiry_Commits()
    {
        var (companyId, employeeId, _, bucketId) = await SeedDueToilAwardAsync(awardedDays: 10m);
        var leaveRequestId = Guid.NewGuid();

        // Commit a 4-day usage against the bucket up front (not part of the race) so expiry's
        // calculation has a real drawdown to account for.
        using (var preScope = _factory.Services.CreateScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var preLedger = new ToilLedgerService(preDb);
            var leaveTypeId = (await preDb.LeaveTypes.SingleAsync(lt => lt.CompanyId == companyId)).Id;
            // occurredOn must be strictly before the bucket's ExpiresOn (seeded one day before AsOf) -
            // otherwise GetOpenBucketsOrderedAsync's "bucket.ExpiresOn <= asOf" guard treats the
            // bucket as already unavailable and this pre-seeding consume fails outright.
            var consumeResult = await preLedger.ConsumeAsync(
                companyId, employeeId, leaveTypeId, 4m, leaveRequestId, Guid.NewGuid(), AsOf.AddDays(-2),
                allowNegativeBalance: false, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.True(consumeResult.IsSuccess);
            await preDb.SaveChangesAsync();
        }

        using var expiryScope = _factory.Services.CreateScope();
        var expiryService = expiryScope.ServiceProvider.GetRequiredService<ToilExpiryService>();

        var reachedPause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        expiryService.TestOnlyAfterLockedLoadAsync = async (_, _, _) =>
        {
            reachedPause.TrySetResult();
            await releasePause.Task;
        };

        var expiryTask = expiryService.ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);
        await reachedPause.Task;

        // Unlike TryConsumeAsync above (which legitimately loses the race outright), a reversal that
        // reads the balance before expiry commits and then blocks on the row lock is *not* logically
        // in conflict with expiry - once unblocked, its stale-versioned save is rejected purely
        // because expiry advanced the Version it read before pausing, not because the two operations
        // are incompatible. A real caller (mirroring the retry-with-a-fresh-scope pattern
        // ToilExpiryJob itself uses - see its class doc comment) re-reads and retries rather than
        // treating that as a genuine race loss, so this local helper does the same: each attempt gets
        // its own scope/DbContext so the retry re-reads the post-expiry ledger/balance state rather
        // than reusing the first attempt's now-stale change tracker.
        async Task<int> ReverseAsync()
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                using var reverseScope = _factory.Services.CreateScope();
                var reverseDb = reverseScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
                var reverseLedger = new ToilLedgerService(reverseDb);

                var reversalResult = await reverseLedger.ReverseAsync(
                    companyId, employeeId, leaveRequestId, Guid.NewGuid(), AsOf, DateTimeOffset.UtcNow, CancellationToken.None);

                try
                {
                    await reverseDb.SaveChangesAsync();
                    return reversalResult.Transactions.Count;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 5)
                {
                    // Blocked on expiry's row lock, then lost the concurrency-token check purely
                    // because expiry's commit advanced Version while this attempt was blocked -
                    // retry with a fresh read of the now-committed state.
                }
            }

            throw new InvalidOperationException("Reversal did not succeed after retries.");
        }

        var reverseTask = ReverseAsync();

        var completedWhilePaused = await Task.WhenAny(reverseTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.NotSame(reverseTask, completedWhilePaused);

        releasePause.TrySetResult();
        var expiryResult = await expiryTask;
        var reversalCount = await reverseTask;

        Assert.Equal(1, expiryResult.TransactionsCreated);
        Assert.Equal(1, reversalCount); // reversal proceeded only after expiry committed

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var expired = await verifyDb.ToilTransactions
            .SingleAsync(t => t.RelatedTransactionId == bucketId && t.Type == ToilTransactionType.Expired);
        // Expiry's calculation only ever saw the 4-day usage that had already committed before it
        // started (never the reversal, which was still blocked) - remaining = 10 - 4 = 6.
        Assert.Equal(6m, expired.Days);

        // 10 awarded - 4 used - 6 expired + 4 reversed (applied strictly after expiry committed).
        var balance = await verifyDb.LeaveBalances.SingleAsync(b => b.CompanyId == companyId);
        Assert.Equal(4m, balance.RemainingDays);
    }

    /// <summary>
    /// P1.1 follow-up: <see cref="ToilExpiryJob"/>'s bounded per-company retry loop. Forces the
    /// first attempt's <c>SaveChangesAsync</c> to fail with <see cref="DbUpdateConcurrencyException"/>
    /// by mutating the tracked balance's concurrency-token <c>OriginalValue</c> (the same technique
    /// <c>DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync</c> uses in production to
    /// pin an expected version) via <see cref="ToilExpiryService.TestOnlyAfterLockedLoadAsync"/> -
    /// genuinely defeating the real row lock from a second connection is impossible (it would simply
    /// block, as proven by the two tests above), so this is the deterministic way to provoke this
    /// exact EF Core failure mode. The job must then retry with a brand new scope/transaction (never
    /// reusing the first attempt's now-faulted change tracker) and succeed on attempt 2.
    /// </summary>
    [Fact]
    public async Task ToilExpiryJob_Retries_With_A_Fresh_Scope_And_Succeeds_After_One_Forced_Concurrency_Conflict()
    {
        var (companyId, _, balanceId, bucketId) = await SeedDueToilAwardAsync(awardedDays: 6m);

        var attemptsSeen = 0;
        void ConfigureService(ToilExpiryService service)
        {
            service.TestOnlyAfterLockedLoadAsync = (dbContext, balances, _) =>
            {
                if (balances.TryGetValue(balanceId, out var balance))
                {
                    var attempt = Interlocked.Increment(ref attemptsSeen);
                    if (attempt == 1)
                    {
                        dbContext.Entry(balance).Property(nameof(IVersionedAggregate.Version)).OriginalValue =
                            balance.Version + 1;
                    }
                }

                return Task.CompletedTask;
            };
        }

        var scopeFactory = new HookInjectingScopeFactory(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), ConfigureService);
        var logger = new CapturingLogger<ToilExpiryJob>();
        var job = new ToilExpiryJob(
            scopeFactory, new FixedClock(AsOf.ToDateTime(TimeOnly.MinValue)), new FixedTimeZoneReader("UTC"), logger);

        await job.ExecuteAsync();

        Assert.Equal(2, attemptsSeen); // attempt 1 (forced failure) then attempt 2 (succeeds)
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(companyId.ToString()));
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(companyId.ToString()));

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var expired = await verifyDb.ToilTransactions
            .SingleAsync(t => t.RelatedTransactionId == bucketId && t.Type == ToilTransactionType.Expired);
        Assert.Equal(6m, expired.Days);

        var balance = await verifyDb.LeaveBalances.SingleAsync(b => b.Id == balanceId);
        Assert.Equal(0m, balance.RemainingDays);
    }

    /// <summary>
    /// P1.1 follow-up: retry exhaustion. Forces every attempt (all
    /// <see cref="ToilExpiryJob"/>'s <c>MaxAttemptsPerCompany</c> = 3) for one company to fail with
    /// <see cref="DbUpdateConcurrencyException"/>, using the same forced-staleness technique as
    /// above. Asserts no partial ledger/balance state leaks for that company, the failure is logged
    /// as an error, and - processed in the very same <see cref="ToilExpiryJob.ExecuteAsync"/> batch
    /// run - a second, healthy company is entirely unaffected and successfully expires its own due
    /// award.
    /// </summary>
    [Fact]
    public async Task ToilExpiryJob_Logs_An_Error_And_Leaves_No_Partial_State_When_Every_Retry_Attempt_Fails_While_A_Healthy_Company_Still_Succeeds()
    {
        var (failingCompanyId, _, failingBalanceId, failingBucketId) = await SeedDueToilAwardAsync(awardedDays: 6m);
        var (healthyCompanyId, _, healthyBalanceId, healthyBucketId) = await SeedDueToilAwardAsync(awardedDays: 5m);

        var attemptsSeen = 0;
        void ConfigureService(ToilExpiryService service)
        {
            service.TestOnlyAfterLockedLoadAsync = (dbContext, balances, _) =>
            {
                if (balances.TryGetValue(failingBalanceId, out var balance))
                {
                    Interlocked.Increment(ref attemptsSeen);
                    // Force every attempt for this company to fail - never let it succeed.
                    dbContext.Entry(balance).Property(nameof(IVersionedAggregate.Version)).OriginalValue =
                        balance.Version + 1;
                }

                return Task.CompletedTask;
            };
        }

        var scopeFactory = new HookInjectingScopeFactory(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), ConfigureService);
        var logger = new CapturingLogger<ToilExpiryJob>();
        var job = new ToilExpiryJob(
            scopeFactory, new FixedClock(AsOf.ToDateTime(TimeOnly.MinValue)), new FixedTimeZoneReader("UTC"), logger);

        await job.ExecuteAsync();

        Assert.Equal(3, attemptsSeen); // MaxAttemptsPerCompany - every attempt forced to fail
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Error && e.Message.Contains(failingCompanyId.ToString()));
        Assert.Equal(
            2,
            logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains(failingCompanyId.ToString())));

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        // No partial ledger/balance changes for the company that exhausted every retry.
        Assert.False(await verifyDb.ToilTransactions.AnyAsync(t => t.RelatedTransactionId == failingBucketId && t.Type == ToilTransactionType.Expired));
        var failingBalance = await verifyDb.LeaveBalances.SingleAsync(b => b.Id == failingBalanceId);
        Assert.Equal(6m, failingBalance.RemainingDays);
        Assert.Equal(1, failingBalance.Version);

        // The healthy company, processed in the same batch run, is entirely unaffected.
        var healthyExpired = await verifyDb.ToilTransactions
            .SingleAsync(t => t.RelatedTransactionId == healthyBucketId && t.Type == ToilTransactionType.Expired);
        Assert.Equal(5m, healthyExpired.Days);
        var healthyBalance = await verifyDb.LeaveBalances.SingleAsync(b => b.Id == healthyBalanceId);
        Assert.Equal(0m, healthyBalance.RemainingDays);
    }

    /// <summary>
    /// Routes every <see cref="ToilExpiryService"/> resolved from the wrapped scope factory through
    /// <paramref name="configureService"/> before returning it, so a test can arm
    /// <see cref="ToilExpiryService.TestOnlyAfterLockedLoadAsync"/> on the fresh instance
    /// <see cref="ToilExpiryJob"/> resolves for every attempt/company, without needing to fork
    /// <see cref="ToilExpiryJob"/>'s own scoping logic. Everything else (the real Postgres-backed
    /// <see cref="LeaveDbContext"/>, real <see cref="ToilLedgerService"/>, etc.) is resolved exactly
    /// as in production.
    /// </summary>
    private sealed class HookInjectingScopeFactory(IServiceScopeFactory inner, Action<ToilExpiryService> configureService)
        : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new HookInjectingScope(inner.CreateScope(), configureService);

        private sealed class HookInjectingScope(IServiceScope inner, Action<ToilExpiryService> configureService) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new HookInjectingServiceProvider(inner.ServiceProvider, configureService);

            public void Dispose() => inner.Dispose();
        }

        private sealed class HookInjectingServiceProvider(IServiceProvider inner, Action<ToilExpiryService> configureService)
            : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                var service = inner.GetService(serviceType);
                if (service is ToilExpiryService toilExpiryService)
                    configureService(toilExpiryService);
                return service;
            }
        }
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FixedTimeZoneReader(string timeZoneId) : ICompanyTimeZoneReader
    {
        public Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(timeZoneId);
    }

    /// <summary>Captures every log entry's level and formatted message for assertions.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
