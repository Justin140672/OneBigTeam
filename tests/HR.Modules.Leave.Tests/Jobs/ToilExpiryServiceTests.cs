using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Leave.Tests.Jobs;

public class ToilExpiryServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly AsOf = new(2026, 7, 1);

    private static LeaveDbContext BuildContext()
    {
        // ExpireCompanyAsync wraps its save in an explicit transaction; the InMemory provider
        // doesn't support transactions and raises a warning-as-error for it by default (same
        // accommodation used by LeaveYearRolloverServiceTests / AdjustLeaveBalanceHandlerTests).
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LeaveDbContext(options);
    }

    private static ToilExpiryService BuildService(LeaveDbContext context, IAuditEventPublisher? auditPublisher = null) =>
        new(context, new FakeClock(FixedUtcNow), auditPublisher ?? new NoOpAuditEventPublisher());

    private static LeaveType CreateToilLeaveType(
        Guid companyId, int? toilExpiryDays = 30, bool isActive = true)
    {
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, Now, hasBalance: true,
            toilExpiryDays: toilExpiryDays);
        if (!isActive)
            leaveType.Deactivate(Now);
        return leaveType;
    }

    private static LeaveBalance CreateBalance(Guid companyId, Guid employeeId, Guid leaveTypeId, decimal awardedDays)
    {
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(), 2026, 0m,
            new DateOnly(2026, 1, 1), Now);
        if (awardedDays != 0)
            balance.Adjust(awardedDays, Now);
        return balance;
    }

    private static ToilTransaction EarnedBucket(
        LeaveBalance balance, decimal days, DateOnly? expiresOn, DateOnly? occurredOn = null) =>
        ToilTransaction.CreateEarned(
            Guid.NewGuid(), balance.CompanyId, balance.EmployeeId, balance.Id, Guid.NewGuid(),
            days, occurredOn ?? new DateOnly(2026, 5, 1), expiresOn, "Overtime", Now);

    private static ToilTransaction UsedDrawdown(ToilTransaction bucket, decimal days) =>
        ToilTransaction.CreateUsed(
            Guid.NewGuid(), bucket.CompanyId, bucket.EmployeeId, bucket.LeaveBalanceId, bucket.Id,
            Guid.NewGuid(), Guid.NewGuid(), days, new DateOnly(2026, 6, 1), "TOIL used", Now);

    private static ToilTransaction ReversalDrawdown(ToilTransaction bucket, decimal days) =>
        ToilTransaction.CreateReversal(
            Guid.NewGuid(), bucket.CompanyId, bucket.EmployeeId, bucket.LeaveBalanceId, bucket.Id,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), days, new DateOnly(2026, 6, 15), "TOIL reversal", Now);

    [Fact]
    public async Task ExpireCompanyAsync_Does_Not_Expire_Award_Before_Its_Expiry_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 2m);
        var bucket = EarnedBucket(balance, 2m, expiresOn: AsOf.AddDays(1));

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(ToilExpiryResult.Empty, result);
        Assert.False(await context.ToilTransactions.AnyAsync(t => t.Type == ToilTransactionType.Expired));
        Assert.Equal(2m, (await context.LeaveBalances.SingleAsync()).RemainingDays);
    }

    [Fact]
    public async Task ExpireCompanyAsync_Expires_Award_On_Its_Expiry_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 2m);
        var bucket = EarnedBucket(balance, 2m, expiresOn: AsOf);

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(1, result.TransactionsCreated);
        var expired = await context.ToilTransactions.SingleAsync(t => t.Type == ToilTransactionType.Expired);
        Assert.Equal(2m, expired.Days);
        Assert.Equal(bucket.Id, expired.RelatedTransactionId);
    }

    [Fact]
    public async Task ExpireCompanyAsync_Deducts_Only_Remaining_After_Usage_And_Reversals()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 10m);
        var bucket = EarnedBucket(balance, 10m, expiresOn: AsOf.AddDays(-1));
        // Used 6, then 1 of that usage reversed => remaining = 10 - 6 + 1 = 5.
        var used = UsedDrawdown(bucket, 6m);
        var reversal = ReversalDrawdown(bucket, 1m);
        balance.RecordUsage(6m, Now);
        balance.Adjust(1m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.AddRange(bucket, used, reversal);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(1, result.TransactionsCreated);
        var expired = await context.ToilTransactions.SingleAsync(t => t.Type == ToilTransactionType.Expired);
        Assert.Equal(5m, expired.Days);
        Assert.Equal(0m, (await context.LeaveBalances.SingleAsync()).RemainingDays);
    }

    [Fact]
    public async Task ExpireCompanyAsync_Fully_Consumed_Award_Produces_No_Deduction()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 4m);
        var bucket = EarnedBucket(balance, 4m, expiresOn: AsOf.AddDays(-2));
        var used = UsedDrawdown(bucket, 4m);
        balance.RecordUsage(4m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.AddRange(bucket, used);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(ToilExpiryResult.Empty, result);
        Assert.False(await context.ToilTransactions.AnyAsync(t => t.Type == ToilTransactionType.Expired));
    }

    [Fact]
    public async Task ExpireCompanyAsync_Previously_Expired_Award_Produces_No_Additional_Deduction()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 0m);
        var bucket = EarnedBucket(balance, 3m, expiresOn: AsOf.AddDays(-10));
        var alreadyExpired = ToilTransaction.CreateExpired(
            Guid.NewGuid(), companyId, employeeId, balance.Id, bucket.Id, Guid.Empty, 3m,
            AsOf.AddDays(-10), "TOIL expired", Now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.AddRange(bucket, alreadyExpired);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(ToilExpiryResult.Empty, result);
        Assert.Equal(1, await context.ToilTransactions.CountAsync(t => t.Type == ToilTransactionType.Expired));
    }

    [Fact]
    public async Task ExpireCompanyAsync_Repeat_Run_Creates_No_Duplicate_Expiry_Transactions()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 2m);
        var bucket = EarnedBucket(balance, 2m, expiresOn: AsOf.AddDays(-1));

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var audit = new CapturingAuditEventPublisher();
        var service = BuildService(context, audit);

        var first = await service.ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);
        var second = await service.ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(1, first.TransactionsCreated);
        Assert.Equal(ToilExpiryResult.Empty, second);
        Assert.Equal(1, await context.ToilTransactions.CountAsync(t => t.Type == ToilTransactionType.Expired));
        Assert.Single(audit.Published);
        Assert.Equal(0m, (await context.LeaveBalances.SingleAsync()).RemainingDays);
    }

    [Fact]
    public async Task ExpireCompanyAsync_Excludes_Inactive_Leave_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId, isActive: false);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 2m);
        var bucket = EarnedBucket(balance, 2m, expiresOn: AsOf.AddDays(-1));

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(ToilExpiryResult.Empty, result);
        Assert.False(await context.ToilTransactions.AnyAsync(t => t.Type == ToilTransactionType.Expired));
    }

    [Fact]
    public async Task ExpireCompanyAsync_Excludes_Leave_Type_Without_Expiry_Configured()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId, toilExpiryDays: null);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 2m);
        // Bucket has an ExpiresOn even though the type isn't currently expiry-configured.
        var bucket = EarnedBucket(balance, 2m, expiresOn: AsOf.AddDays(-1));

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(ToilExpiryResult.Empty, result);
        Assert.False(await context.ToilTransactions.AnyAsync(t => t.Type == ToilTransactionType.Expired));
    }

    [Fact]
    public async Task ExpireCompanyAsync_Excludes_Buckets_Without_ExpiresOn()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 2m);
        var bucket = EarnedBucket(balance, 2m, expiresOn: null);

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(ToilExpiryResult.Empty, result);
    }

    [Fact]
    public async Task ExpireCompanyAsync_Excludes_Other_Companies()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var otherLeaveType = CreateToilLeaveType(otherCompanyId);
        var otherBalance = CreateBalance(otherCompanyId, employeeId, otherLeaveType.Id, 2m);
        var otherBucket = EarnedBucket(otherBalance, 2m, expiresOn: AsOf.AddDays(-1));

        context.LeaveTypes.Add(otherLeaveType);
        context.LeaveBalances.Add(otherBalance);
        context.ToilTransactions.Add(otherBucket);
        await context.SaveChangesAsync();

        var result = await BuildService(context).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(ToilExpiryResult.Empty, result);
        Assert.False(await context.ToilTransactions.AnyAsync(t => t.Type == ToilTransactionType.Expired));
    }

    [Fact]
    public async Task ExpireCompanyAsync_Persists_Balance_Ledger_Transaction_And_Audit_Event()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = CreateToilLeaveType(companyId);
        var balance = CreateBalance(companyId, employeeId, leaveType.Id, 8m);
        var bucket = EarnedBucket(balance, 8m, expiresOn: AsOf.AddDays(-3), occurredOn: new DateOnly(2026, 4, 15));
        var used = UsedDrawdown(bucket, 3m);
        balance.RecordUsage(3m, Now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.AddRange(bucket, used);
        await context.SaveChangesAsync();

        var audit = new CapturingAuditEventPublisher();
        var result = await BuildService(context, audit).ExpireCompanyAsync(companyId, AsOf, CancellationToken.None);

        Assert.Equal(1, result.TransactionsCreated);

        var persistedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, persistedBalance.RemainingDays);

        var expired = await context.ToilTransactions.SingleAsync(t => t.Type == ToilTransactionType.Expired);
        Assert.Equal(companyId, expired.CompanyId);
        Assert.Equal(employeeId, expired.EmployeeId);
        Assert.Equal(balance.Id, expired.LeaveBalanceId);
        Assert.Equal(bucket.Id, expired.RelatedTransactionId);
        Assert.Equal(5m, expired.Days);
        Assert.Equal(AsOf, expired.OccurredOn);
        Assert.Equal(ToilExpiryService.SystemActorId, expired.ActorEmployeeId);
        Assert.Contains("15 Apr 2026", expired.Description);

        var published = Assert.Single(audit.Published);
        var auditEvent = Assert.IsType<ToilExpiredAuditEvent>(published);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(expired.Id, auditEvent.TransactionId);
        Assert.Equal(balance.Id, auditEvent.LeaveBalanceId);
        Assert.Equal(bucket.Id, auditEvent.BucketTransactionId);
        Assert.Equal(5m, auditEvent.Days);
        Assert.Equal(AsOf, auditEvent.OccurredOn);
    }
}
