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

// Date-gating tests for LeaveYearRolloverJob (LEAVE-03). LeaveYearRolloverService itself is
// exercised directly and in full by LeaveYearRolloverServiceTests — these tests only need to
// prove the job invokes (or skips) that service on the correct company-local day, so each
// scenario here uses the minimal previous-year balance data required for a rollover to actually
// happen when the job does call through.
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
        var rolloverService = new LeaveYearRolloverService(context, clock, auditPublisher ?? new NoOpAuditEventPublisher());

        return new LeaveYearRolloverJob(
            context,
            clock,
            new FakeCompanyLeaveSettingsReader(settings),
            new FakeCompanyTimeZoneReader(timeZoneId),
            rolloverService,
            NullLogger<LeaveYearRolloverJob>.Instance);
    }

    // Seeds enough data (a leave type, policy, previous-year balance, and an assignment so the
    // company is scanned at all) that RolloverCompanyAsync will actually create a new-year balance
    // if the job calls through on this run.
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
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id, previousPolicyYear, 25m, now);
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
        var fixedUtcNow = new DateTime(2027, 1, 2, 9, 0, 0, DateTimeKind.Utc); // one day after rollover day
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
        // The company's policy year that started April 2026 is labelled 2026; the one starting
        // today (April 2027) is labelled 2027 (matches LeaveYearCalculator.GetPolicyYear).
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
        // Proves the job doesn't hard-code January 1st as "the" rollover day — for a non-January
        // leave year, January 1st is just an ordinary day mid-policy-year.
        var fixedUtcNow = new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var (companyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2025, now);
        await context.SaveChangesAsync();

        var settings = CompanyLeaveSettings.Default with { LeaveYearStartMonth = 4 };
        var job = BuildJob(context, fixedUtcNow, settings, timeZoneId: "UTC");

        await job.ExecuteAsync();

        // The seeded previous-year (2025) balance always exists — assert specifically that no new
        // 2026 policy-year balance was created (2026 is the company's *current*, not-yet-rolled
        // policy year as of Jan 1 2027 for an April-start company; rollover isn't due until April).
        Assert.False(await context.LeaveBalances.AnyAsync(b => b.CompanyId == companyId && b.PolicyYear == 2026));
    }

    [Fact]
    public async Task ExecuteAsync_Uses_Company_Local_Day_Not_UTC_Day_When_Determining_Rollover_Day_Is_Due()
    {
        // 2026-12-31T23:30:00Z is still Dec 31 in UTC, but already Jan 1 00:30 in Europe/London
        // (GMT+0 in winter — but this scenario uses a time zone genuinely ahead of UTC at this
        // instant to prove company-local resolution, mirroring ProcessLeavingEmployeesJobTests'
        // BST-transition test). Use Pacific/Auckland (UTC+13 in Dec, DST) as a always-ahead zone.
        var fixedUtcNow = new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc); // 2027-01-01 01:00 in Auckland (UTC+13)
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
        // Limitation: forcing RolloverCompanyAsync itself to throw for one company without
        // modifying production code (e.g. via a seam to inject a faulty service) isn't practical
        // here, since LeaveYearRolloverJob takes a concrete LeaveYearRolloverService rather than an
        // interface. This test instead proves the job iterates and evaluates every company
        // independently in a single run — one company not being due for rollover does not stop a
        // second, due, company in the same batch from being processed. Combined with the try/catch
        // being visible directly in LeaveYearRolloverJob.ExecuteAsync (read during implementation
        // review), this gives confidence in the isolation behaviour without an achievable direct
        // exception-injection test.
        var fixedUtcNow = new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var (notDueCompanyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2025, now); // April-start, not due today
        var (dueCompanyId, _) = SeedRolloverableCompany(context, previousPolicyYear: 2026, now); // calendar-year, due today
        await context.SaveChangesAsync();

        var job = new LeaveYearRolloverJob(
            context,
            new FakeClock(fixedUtcNow),
            new PerCompanyLeaveSettingsReader(notDueCompanyId, 4, dueCompanyId, 1),
            new FakeCompanyTimeZoneReader("UTC"),
            new LeaveYearRolloverService(context, new FakeClock(fixedUtcNow), new NoOpAuditEventPublisher()),
            NullLogger<LeaveYearRolloverJob>.Instance);

        await job.ExecuteAsync();

        // The seeded previous-year (2025) balance for notDueCompanyId always exists — assert
        // specifically that no new 2026 policy-year balance was created for it (not due until
        // April), while dueCompanyId (calendar-year) did roll over to 2027 in the same run.
        Assert.False(await context.LeaveBalances.AnyAsync(b => b.CompanyId == notDueCompanyId && b.PolicyYear == 2026));
        Assert.True(await context.LeaveBalances.AnyAsync(b => b.CompanyId == dueCompanyId && b.PolicyYear == 2027));
    }
}

// Test-only reader returning different LeaveYearStartMonth settings per company, used to exercise
// two companies with different rollover-day outcomes in a single job run.
internal sealed class PerCompanyLeaveSettingsReader(
    Guid companyIdA, int startMonthA, Guid companyIdB, int startMonthB) : ICompanyLeaveSettingsReader
{
    public Task<CompanyLeaveSettings> GetLeaveSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var startMonth = companyId == companyIdA ? startMonthA : startMonthB;
        return Task.FromResult(CompanyLeaveSettings.Default with { LeaveYearStartMonth = startMonth });
    }
}
