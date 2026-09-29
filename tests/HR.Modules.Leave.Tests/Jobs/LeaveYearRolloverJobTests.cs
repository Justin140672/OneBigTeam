using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Leave.Tests.Jobs;

public class LeaveYearRolloverJobTests
{
    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LeaveDbContext(options);
    }

    private static LeaveYearRolloverJob BuildJob(
        LeaveDbContext context,
        DateTime fixedUtcNow,
        CompanyLeaveSettings settings,
        string timeZoneId = "UTC",
        IAuditEventPublisher? auditPublisher = null)
    {
        var clock = new FakeClock(fixedUtcNow);
        var rolloverService = new LeaveYearRolloverService(
            context, clock, new FakeCompanyLeaveSettingsReader(settings), auditPublisher ?? new NoOpAuditEventPublisher());

        return new LeaveYearRolloverJob(
            context,
            clock,
            new FakeCompanyLeaveSettingsReader(settings),
            new FakeCompanyTimeZoneReader(timeZoneId),
            rolloverService,
            NullLogger<LeaveYearRolloverJob>.Instance);
    }

    private static (Guid CompanyId, Guid EmployeeId) SeedRolloverableCompany(
        LeaveDbContext context, int previousPolicyYear, DateTimeOffset now)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly,
            LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard", null, 5, false, false, now);
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, previousPolicyYear, 25m,
            new DateOnly(previousPolicyYear, 1, 1), now);
        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, policy.Id, new DateOnly(previousPolicyYear, 1, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        context.EmployeeLeavePolicyAssignments.Add(assignment);

        return (companyId, employeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Rolls_Over_CalendarYear_Company_On_January_First()
    {
        var fixedUtcNow = new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var (companyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2026, now);
        await context.SaveChangesAsync();

        var settings = CompanyLeaveSettings.Default with { LeaveYearStartMonth = 1 };
        var job = BuildJob(context, fixedUtcNow, settings, timeZoneId: "UTC");

        await job.ExecuteAsync();

        Assert.True(await context.LeaveBalances.AnyAsync(b => b.CompanyId == companyId && b.PolicyYear == 2027));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Roll_Over_CalendarYear_Company_On_Any_Other_Day()
    {
        var fixedUtcNow = new DateTime(2027, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var (companyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2026, now);
        await context.SaveChangesAsync();

        var settings = CompanyLeaveSettings.Default with { LeaveYearStartMonth = 1 };
        var job = BuildJob(context, fixedUtcNow, settings, timeZoneId: "UTC");

        await job.ExecuteAsync();

        Assert.False(await context.LeaveBalances.AnyAsync(b => b.CompanyId == companyId && b.PolicyYear == 2027));
    }

    [Fact]
    public async Task ExecuteAsync_Rolls_Over_AprilStart_Company_On_April_First()
    {
        var fixedUtcNow = new DateTime(2027, 4, 1, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var (companyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2026, now);
        await context.SaveChangesAsync();

        var settings = CompanyLeaveSettings.Default with { LeaveYearStartMonth = 4 };
        var job = BuildJob(context, fixedUtcNow, settings, timeZoneId: "UTC");

        await job.ExecuteAsync();

        Assert.True(await context.LeaveBalances.AnyAsync(b => b.CompanyId == companyId && b.PolicyYear == 2027));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Roll_Over_AprilStart_Company_On_January_First()
    {
        var fixedUtcNow = new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var (companyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2025, now);
        await context.SaveChangesAsync();

        var settings = CompanyLeaveSettings.Default with { LeaveYearStartMonth = 4 };
        var job = BuildJob(context, fixedUtcNow, settings, timeZoneId: "UTC");

        await job.ExecuteAsync();

        Assert.False(await context.LeaveBalances.AnyAsync(b => b.CompanyId == companyId && b.PolicyYear == 2026));
    }

    [Fact]
    public async Task ExecuteAsync_Uses_Company_Local_Day_Not_UTC_Day_When_Determining_Rollover_Day_Is_Due()
    {
        var fixedUtcNow = new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var (companyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2026, now);
        await context.SaveChangesAsync();

        var settings = CompanyLeaveSettings.Default with { LeaveYearStartMonth = 1 };
        var job = BuildJob(context, fixedUtcNow, settings, timeZoneId: "Pacific/Auckland");

        await job.ExecuteAsync();

        Assert.True(await context.LeaveBalances.AnyAsync(b => b.CompanyId == companyId && b.PolicyYear == 2027));
    }

    [Fact]
    public async Task ExecuteAsync_Continues_Processing_Remaining_Companies_When_Scanned_But_Not_Due()
    {
        var fixedUtcNow = new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var (notDueCompanyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2025, now);
        var (dueCompanyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2026, now);
        await context.SaveChangesAsync();

        var job = new LeaveYearRolloverJob(
            context,
            new FakeClock(fixedUtcNow),
            new PerCompanyLeaveSettingsReader(notDueCompanyId, 4, 1),
            new FakeCompanyTimeZoneReader("UTC"),
            new LeaveYearRolloverService(
                context, new FakeClock(fixedUtcNow),
                new PerCompanyLeaveSettingsReader(notDueCompanyId, 4, 1),
                new NoOpAuditEventPublisher()),
            NullLogger<LeaveYearRolloverJob>.Instance);

        await job.ExecuteAsync();

        Assert.False(await context.LeaveBalances.AnyAsync(b => b.CompanyId == notDueCompanyId && b.PolicyYear == 2026));
        Assert.True(await context.LeaveBalances.AnyAsync(b => b.CompanyId == dueCompanyId && b.PolicyYear == 2027));
    }
}

internal sealed class PerCompanyLeaveSettingsReader(
    Guid companyIdA, int startMonthA, int startMonthB) : ICompanyLeaveSettingsReader
{
    public Task<CompanyLeaveSettings> GetLeaveSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var startMonth = companyId == companyIdA ? startMonthA : startMonthB;
        return Task.FromResult(CompanyLeaveSettings.Default with { LeaveYearStartMonth = startMonth });
    }
}
