using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmployeeProfileAndEmployment;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Ticket 2 (item 5): the combined Employee Edit save. Both the profile half and the employment
// half are applied to one tracked Employee aggregate and committed with a single
// SaveChangesWithConcurrencyAsync — a conflict rolls back everything and publishes no events.
public class UpdateEmployeeProfileAndEmploymentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static DbContextOptions<EmployeesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<EmployeesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static EmployeesDbContext BuildContext()
        => new(Options(Guid.NewGuid().ToString("N")));

    private static Employee NewEmployee(Guid companyId)
        => Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));

    private static UpdateEmployeeProfileAndEmploymentHandler Handler(
        EmployeesDbContext ctx,
        FakeAuditPublisher? audit = null,
        CapturingIntegrationEventPublisher? integration = null)
        => new(
            ctx,
            new FakeClock(FixedUtcNow),
            new FakeCompanyContactValidationReader(),
            new FakeCompanyEmployeeNumberSettingsReader(),
            audit ?? new FakeAuditPublisher(),
            integration ?? new CapturingIntegrationEventPublisher());

    private static UpdateEmployeeProfileAndEmploymentRequest Request(
        Guid companyId, Guid id, int? expectedVersion,
        string firstName = "Alicia", string? notes = "combined-note", Guid? correlationId = null)
        => new()
        {
            CompanyId = companyId,
            Id = id,
            FirstName = firstName,
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            HasSystemAccess = true,
            EmployeeNumber = "EMP-0001",
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            Notes = notes,
            ExpectedVersion = expectedVersion,
            CorrelationId = correlationId,
        };

    [Fact]
    public async Task HandleAsync_Applies_Both_Halves_And_Returns_Incremented_Version()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employee = NewEmployee(companyId);

        await using (var seed = new EmployeesDbContext(Options(dbName)))
        {
            seed.Employees.Add(employee);
            await seed.SaveChangesAsync();
        }

        await using var ctx = new EmployeesDbContext(Options(dbName));
        var result = await Handler(ctx).HandleAsync(
            Request(companyId, employee.Id, expectedVersion: 1, firstName: "Alicia", notes: "merged"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alicia", result.Value!.FirstName);   // profile half
        Assert.Equal("merged", result.Value.Notes);         // employment half
        Assert.Equal(2, result.Value.Version);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Equal("Alicia", saved.FirstName);
        Assert.Equal("merged", saved.Notes);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Both_Profile_And_Employment_Audit_Events_With_Same_CorrelationId()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = NewEmployee(companyId);
        ctx.Employees.Add(employee);
        await ctx.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        var correlationId = Guid.NewGuid();

        var result = await Handler(ctx, audit).HandleAsync(
            Request(companyId, employee.Id, expectedVersion: 1, firstName: "Alicia", notes: "n", correlationId: correlationId),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, audit.Published.Count);
        Assert.Contains(audit.Published, e => e.EventType == "employee.profile.updated");
        Assert.Contains(audit.Published, e => e.EventType == "employee.employment-details.updated");
        Assert.All(audit.Published, e => Assert.Equal(correlationId, e.CorrelationId));
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Employee()
    {
        await using var ctx = BuildContext();

        var result = await Handler(ctx).HandleAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), expectedVersion: 1),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Stale_ExpectedVersion_Returns_Concurrency_And_Persists_Nothing_And_Publishes_Nothing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employee = NewEmployee(companyId);

        await using (var seed = new EmployeesDbContext(Options(dbName)))
        {
            seed.Employees.Add(employee);
            await seed.SaveChangesAsync();
        }

        // Context A tracks the row at Version 1.
        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.Employees.SingleAsync();

        // Context B wins the race, bumping the store to Version 2.
        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await Handler(ctxB).HandleAsync(
                Request(companyId, employee.Id, expectedVersion: 1, firstName: "Winner", notes: "winner-note"),
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var integration = new CapturingIntegrationEventPublisher();

        var result = await Handler(ctxA, audit, integration).HandleAsync(
            Request(companyId, employee.Id, expectedVersion: 1, firstName: "Loser", notes: "loser-note"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        Assert.Empty(integration.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Equal("Winner", saved.FirstName);       // profile field unchanged by loser
        Assert.Equal("winner-note", saved.Notes);       // employment field unchanged by loser
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task HandleAsync_Null_ExpectedVersion_Returns_Concurrency_And_Persists_Nothing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employee = NewEmployee(companyId);

        await using (var seed = new EmployeesDbContext(Options(dbName)))
        {
            seed.Employees.Add(employee);
            await seed.SaveChangesAsync();
        }

        var audit = new FakeAuditPublisher();
        await using var ctx = new EmployeesDbContext(Options(dbName));

        var result = await Handler(ctx, audit).HandleAsync(
            Request(companyId, employee.Id, expectedVersion: null, firstName: "NoVersion", notes: "x"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Equal("Alice", saved.FirstName);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task HandleAsync_Mid_Operation_Concurrent_Modification_Rolls_Everything_Back()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employee = NewEmployee(companyId);

        await using (var seed = new EmployeesDbContext(Options(dbName)))
        {
            seed.Employees.Add(employee);
            await seed.SaveChangesAsync();
        }

        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.Employees.SingleAsync();

        // A second context bumps the row's version between context A's load and its save.
        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var other = await ctxB.Employees.SingleAsync();
            other.UpdateProfile("Concurrent", "Editor", "alice@example.com", null, StartDate,
                new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
            other.IncrementVersion();
            await ctxB.SaveChangesAsync();
        }

        var result = await Handler(ctxA).HandleAsync(
            Request(companyId, employee.Id, expectedVersion: 1, firstName: "Loser", notes: "loser"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Equal("Concurrent", saved.FirstName);
        Assert.NotEqual("loser", saved.Notes);
    }
}
