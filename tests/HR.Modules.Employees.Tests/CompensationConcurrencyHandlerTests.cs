using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateFutureCompensationRecord;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Ticket 2 (rollout): optimistic-concurrency (Compensation.Version) coverage for the
// UpdateFutureCompensationRecord slice. Mirrors HR.Modules.Employees.Tests/EmployeeConcurrencyHandlerTests
// — the stale-version paths use two DbContext instances pointed at the same EF InMemory database so
// that context B saves first (genuinely bumping the store's Version via IncrementVersion) and the
// handler under test then saves against context A with a now-stale ExpectedVersion, raising
// DbUpdateConcurrencyException exactly as real Postgres would.
public class CompensationConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorEmployeeId = Guid.NewGuid();
    private static readonly DateOnly FutureEffectiveFrom = new(2027, 1, 1);

    private static DbContextOptions<EmployeesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<EmployeesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static UpdateFutureCompensationRecordHandler BuildHandler(
        EmployeesDbContext context, FakeAuditPublisher publisher)
        => new(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), publisher);

    private static (Employee Employee, Compensation Record) NewFixture(Guid companyId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1),
            true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        var record = Compensation.Create(
            Guid.NewGuid(), companyId, employee.Id, FutureEffectiveFrom, SalaryType.Annual, 45000m, "GBP",
            null, null, "Original", CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        return (employee, record);
    }

    private static UpdateFutureCompensationRecordRequest Request(
        Guid companyId, Guid employeeId, Guid id, int? expectedVersion, decimal salary = 50000m)
        => new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = id,
            SalaryType = SalaryType.Annual,
            Salary = salary,
            Currency = "GBP",
            Reason = CompensationChangeReason.Correction,
            ExpectedVersion = expectedVersion,
        };

    private static async Task<(string DbName, Guid CompanyId, Employee Employee, Compensation Record)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var (employee, record) = NewFixture(companyId);

        await using var seed = new EmployeesDbContext(Options(dbName));
        seed.Employees.Add(employee);
        seed.Compensations.Add(record);
        await seed.SaveChangesAsync();

        return (dbName, companyId, employee, record);
    }

    // ── Compensation.IncrementVersion ─────────────────────────────────────────

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var (_, record) = NewFixture(Guid.NewGuid());
        Assert.Equal(1, record.Version);

        record.IncrementVersion();
        Assert.Equal(2, record.Version);

        record.IncrementVersion();
        Assert.Equal(3, record.Version);
    }

    // ── Matching ExpectedVersion ─────────────────────────────────────────────

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, employee, record) = await SeedAsync();

        await using var ctx = new EmployeesDbContext(Options(dbName));
        var publisher = new FakeAuditPublisher();
        var result = await BuildHandler(ctx, publisher).HandleAsync(
            Request(companyId, employee.Id, record.Id, expectedVersion: 1, salary: 55000m),
            ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal(55000m, result.Value.Salary);
        Assert.Single(publisher.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Compensations.SingleAsync();
        Assert.Equal(2, saved.Version);
        Assert.Equal(55000m, saved.Salary);
    }

    // ── Null ExpectedVersion — last-writer-wins ───────────────────────────────

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, employee, record) = await SeedAsync();

        var publisher = new FakeAuditPublisher();
        await using var ctx = new EmployeesDbContext(Options(dbName));
        var result = await BuildHandler(ctx, publisher).HandleAsync(
            Request(companyId, employee.Id, record.Id, expectedVersion: null, salary: 70000m),
            ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(publisher.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Compensations.SingleAsync();
        Assert.Equal(45000m, saved.Salary);
        Assert.Equal(1, saved.Version);
    }

    // ── Stale ExpectedVersion — concurrency failure ───────────────────────────

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
    {
        var (dbName, companyId, employee, record) = await SeedAsync();

        // Context A loads and tracks the row while Version == 1.
        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.Compensations.SingleAsync();

        // Context B wins the race: saves first, bumping the store's Version to 2.
        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await BuildHandler(ctxB, new FakeAuditPublisher()).HandleAsync(
                Request(companyId, employee.Id, record.Id, expectedVersion: 1, salary: 61000m),
                ActorEmployeeId, CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        // Context A now saves with the stale ExpectedVersion == 1.
        var publisher = new FakeAuditPublisher();
        var result = await BuildHandler(ctxA, publisher).HandleAsync(
            Request(companyId, employee.Id, record.Id, expectedVersion: 1, salary: 99000m),
            ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(publisher.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Compensations.SingleAsync();
        Assert.Equal(61000m, saved.Salary);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task Stale_Save_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, employee, record) = await SeedAsync();

        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.Compensations.SingleAsync();

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            await BuildHandler(ctxB, new FakeAuditPublisher()).HandleAsync(
                Request(companyId, employee.Id, record.Id, expectedVersion: 1, salary: 62000m),
                ActorEmployeeId, CancellationToken.None);
        }

        var publisher = new FakeAuditPublisher();
        var result = await BuildHandler(ctxA, publisher).HandleAsync(
            Request(companyId, employee.Id, record.Id, expectedVersion: 1, salary: 88000m),
            ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(publisher.Published);
    }
}
