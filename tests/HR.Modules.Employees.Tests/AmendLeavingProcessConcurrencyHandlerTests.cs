using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AmendLeavingProcess;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Ticket 2 (optimistic concurrency rollout): EmployeeLeavingProcess.Version coverage for the
// AmendLeavingProcess slice. Mirrors EmployeeConcurrencyHandlerTests — the stale-version paths use
// two DbContext instances pointed at the same EF InMemory database so that context B saves first
// (genuinely bumping the store's Version via IncrementVersion) and the handler under test then
// saves against context A with a now-stale ExpectedVersion, raising DbUpdateConcurrencyException
// exactly as real Postgres would. All amendments here use a future LeavingDate so the backdated
// departure-finalisation path never runs.
public class AmendLeavingProcessConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly FutureLeavingDate = new(2026, 12, 1);

    private static DbContextOptions<EmployeesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<EmployeesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static EmployeeLeavingProcess NewLeavingProcess(Guid companyId, Guid employeeId)
        => EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 31),
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));

    private static AmendLeavingProcessHandler BuildHandler(
        EmployeesDbContext context, FakeAuditPublisher audit, CapturingIntegrationEventPublisher integration)
    {
        var offboardingStatusReader = new FakeOffboardingStatusReader();
        var departureFinalizer = new EmployeeDepartureFinalizer(
            context,
            audit,
            new NoOpIntegrationEventPublisher(),
            offboardingStatusReader,
            new FakeCompanyLeavingSettingsReader(),
            new FakeNotificationWriter(),
            new FakeEmployeeTimelineWriter(),
            new FakeDirectReportsReader());

        return new AmendLeavingProcessHandler(
            context,
            new FakeClock(FixedUtcNow),
            new FakeCompanyTimeZoneReader(),
            audit,
            integration,
            offboardingStatusReader,
            departureFinalizer);
    }

    private static AmendLeavingProcessRequest Request(Guid companyId, Guid employeeId, int? expectedVersion, DateOnly? leavingDate = null)
        => new(
            companyId,
            employeeId,
            LeavingDate: leavingDate ?? FutureLeavingDate,
            LastWorkingDay: (leavingDate ?? FutureLeavingDate).AddDays(-1),
            LeavingReason.MutualAgreement,
            ConfirmBackdatedLeavingDate: false,
            ExpectedVersion: expectedVersion);

    private static async Task<(string DbName, Guid CompanyId, Guid EmployeeId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using var seed = new EmployeesDbContext(Options(dbName));
        seed.EmployeeLeavingProcesses.Add(NewLeavingProcess(companyId, employeeId));
        await seed.SaveChangesAsync();

        return (dbName, companyId, employeeId);
    }

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var process = NewLeavingProcess(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(1, process.Version);

        process.IncrementVersion();
        Assert.Equal(2, process.Version);
    }

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, employeeId) = await SeedAsync();

        await using var ctx = new EmployeesDbContext(Options(dbName));
        var result = await BuildHandler(ctx, new FakeAuditPublisher(), new CapturingIntegrationEventPublisher())
            .HandleAsync(Request(companyId, employeeId, expectedVersion: 1), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new EmployeesDbContext(Options(dbName));
        Assert.Equal(2, (await verify.EmployeeLeavingProcesses.SingleAsync()).Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, employeeId) = await SeedAsync();

        var audit = new FakeAuditPublisher();
        var integration = new CapturingIntegrationEventPublisher();
        await using var ctx = new EmployeesDbContext(Options(dbName));
        var result = await BuildHandler(ctx, audit, integration)
            .HandleAsync(Request(companyId, employeeId, expectedVersion: null, leavingDate: new DateOnly(2027, 1, 15)), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        Assert.Empty(integration.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(new DateOnly(2026, 8, 1), saved.LeavingDate);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_Nothing()
    {
        var (dbName, companyId, employeeId) = await SeedAsync();

        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.EmployeeLeavingProcesses.SingleAsync();

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await BuildHandler(ctxB, new FakeAuditPublisher(), new CapturingIntegrationEventPublisher())
                .HandleAsync(Request(companyId, employeeId, expectedVersion: 1, leavingDate: new DateOnly(2026, 12, 15)), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var integration = new CapturingIntegrationEventPublisher();
        var result = await BuildHandler(ctxA, audit, integration)
            .HandleAsync(Request(companyId, employeeId, expectedVersion: 1, leavingDate: new DateOnly(2026, 12, 20)), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        Assert.Empty(integration.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.EmployeeLeavingProcesses.SingleAsync();
        Assert.Equal(new DateOnly(2026, 12, 15), saved.LeavingDate);
        Assert.Equal(2, saved.Version);
    }
}
