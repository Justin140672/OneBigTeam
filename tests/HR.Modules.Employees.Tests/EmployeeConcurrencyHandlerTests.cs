using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmployeeProfile;
using HR.Modules.Employees.Features.UpdateEmploymentDetails;
using HR.Modules.Employees.Features.UpdateMyContactDetails;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Ticket 2: optimistic-concurrency (Employee.Version) coverage for the three user-facing employee
// edit handlers. The stale-version paths use two DbContext instances pointed at the same EF
// InMemory database — the same technique proven in
// HR.Modules.Notifications.Tests/EmailDeliveryJobTests.SendAsync_Concurrency_Conflict_On_Claim_Step:
// context B saves first (genuinely bumping the store's Version via IncrementVersion), then the
// handler under test saves against context A with a now-stale ExpectedVersion, so EF Core raises
// DbUpdateConcurrencyException exactly as real Postgres would.
public class EmployeeConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static DbContextOptions<EmployeesDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<EmployeesDbContext>().UseInMemoryDatabase(dbName).Options;

    private static Employee NewEmployee(Guid companyId)
        => Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));

    private static UpdateEmployeeProfileHandler ProfileHandler(EmployeesDbContext ctx)
        => new(ctx, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(),
            new FakeAuditPublisher(), new NoOpIntegrationEventPublisher());

    private static UpdateEmploymentDetailsHandler EmploymentHandler(EmployeesDbContext ctx)
        => new(ctx, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeAuditPublisher(), new FakeCompanyEmployeeNumberSettingsReader());

    private static UpdateMyContactDetailsHandler ContactHandler(EmployeesDbContext ctx)
        => new(ctx, new FakeClock(FixedUtcNow), new FakeAuditPublisher(),
            new FakeCompanyContactValidationReader());

    private static UpdateEmployeeProfileRequest ProfileRequest(Guid companyId, Guid id, int? expectedVersion, string firstName = "Alicia")
        => new()
        {
            CompanyId = companyId,
            Id = id,
            FirstName = firstName,
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = StartDate,
            ExpectedVersion = expectedVersion,
        };

    private static UpdateEmploymentDetailsRequest EmploymentRequest(Guid companyId, Guid id, int? expectedVersion, string? notes = "updated")
        => new()
        {
            CompanyId = companyId,
            Id = id,
            Status = EmploymentStatus.Active,
            StartDate = StartDate,
            Notes = notes,
            ExpectedVersion = expectedVersion,
        };

    private static UpdateMyContactDetailsRequest ContactRequest(Guid companyId, int? expectedVersion, string city = "London")
        => new()
        {
            CompanyId = companyId,
            AddressLine1 = "1 Test Street",
            City = city,
            PostCode = "SW1A 1AA",
            Country = "United Kingdom",
            ExpectedVersion = expectedVersion,
        };

    // ── Employee.IncrementVersion ───────────────────────────────────────────────

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var employee = NewEmployee(Guid.NewGuid());
        Assert.Equal(1, employee.Version);

        employee.IncrementVersion();
        Assert.Equal(2, employee.Version);

        employee.IncrementVersion();
        Assert.Equal(3, employee.Version);
    }

    // ── UpdateEmployeeProfile ──────────────────────────────────────────────────

    [Fact]
    public async Task Profile_Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
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
        var result = await ProfileHandler(ctx).HandleAsync(
            ProfileRequest(companyId, employee.Id, expectedVersion: 1), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);

        await using var verify = new EmployeesDbContext(Options(dbName));
        Assert.Equal(2, (await verify.Employees.SingleAsync()).Version);
    }

    [Fact]
    public async Task Profile_Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
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
        var result = await ProfileHandler(ctx).HandleAsync(
            ProfileRequest(companyId, employee.Id, expectedVersion: null, firstName: "Second"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Equal("Alice", saved.FirstName);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Profile_Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employee = NewEmployee(companyId);

        await using (var seed = new EmployeesDbContext(Options(dbName)))
        {
            seed.Employees.Add(employee);
            await seed.SaveChangesAsync();
        }

        // Context A loads and tracks the row while Version == 1.
        await using var ctxA = new EmployeesDbContext(Options(dbName));
        await ctxA.Employees.SingleAsync();

        // Context B wins the race: saves first, bumping the store's Version to 2.
        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await ProfileHandler(ctxB).HandleAsync(
                ProfileRequest(companyId, employee.Id, expectedVersion: 1, firstName: "Winner"), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        // Context A now saves with the stale ExpectedVersion == 1.
        var result = await ProfileHandler(ctxA).HandleAsync(
            ProfileRequest(companyId, employee.Id, expectedVersion: 1, firstName: "Loser"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Equal("Winner", saved.FirstName);
        Assert.Equal(2, saved.Version);
    }

    // ── UpdateEmploymentDetails ────────────────────────────────────────────────

    [Fact]
    public async Task Employment_Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
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
        var result = await EmploymentHandler(ctx).HandleAsync(
            EmploymentRequest(companyId, employee.Id, expectedVersion: 1), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
    }

    [Fact]
    public async Task Employment_Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
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
        var result = await EmploymentHandler(ctx).HandleAsync(
            EmploymentRequest(companyId, employee.Id, expectedVersion: null, notes: "no-check"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.NotEqual("no-check", saved.Notes);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Employment_Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
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

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await EmploymentHandler(ctxB).HandleAsync(
                EmploymentRequest(companyId, employee.Id, expectedVersion: 1, notes: "winner-notes"), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var result = await EmploymentHandler(ctxA).HandleAsync(
            EmploymentRequest(companyId, employee.Id, expectedVersion: 1, notes: "loser-notes"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Equal("winner-notes", saved.Notes);
        Assert.Equal(2, saved.Version);
    }

    // ── UpdateMyContactDetails (self-service) ──────────────────────────────────

    [Fact]
    public async Task Contact_Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
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
        var result = await ContactHandler(ctx).HandleAsync(
            ContactRequest(companyId, expectedVersion: 1), employee.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
    }

    [Fact]
    public async Task Contact_Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
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
        var result = await ContactHandler(ctx).HandleAsync(
            ContactRequest(companyId, expectedVersion: null, city: "Manchester"), employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Null(saved.City);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Contact_Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Leaves_Row_Unchanged()
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

        // Concurrent writer: an HR admin edits the same employee's profile, bumping Version to 2.
        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            var winner = await ProfileHandler(ctxB).HandleAsync(
                ProfileRequest(companyId, employee.Id, expectedVersion: 1, firstName: "AdminEdited"), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var result = await ContactHandler(ctxA).HandleAsync(
            ContactRequest(companyId, expectedVersion: 1, city: "StaleCity"), employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new EmployeesDbContext(Options(dbName));
        var saved = await verify.Employees.SingleAsync();
        Assert.Equal("AdminEdited", saved.FirstName);
        Assert.Null(saved.City);
        Assert.Equal(2, saved.Version);
    }

    // ── Rejected saves publish no audit / integration events ───────────────────
    // SaveChangesWithConcurrencyAsync commits nothing on conflict, so the handlers must return
    // before their PublishAsync calls. These pin that contract per handler.

    [Fact]
    public async Task Profile_Stale_Save_Publishes_No_Audit_Or_Integration_Events()
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

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            await ProfileHandler(ctxB).HandleAsync(
                ProfileRequest(companyId, employee.Id, expectedVersion: 1, firstName: "Winner"), Guid.NewGuid(), CancellationToken.None);
        }

        var audit = new FakeAuditPublisher();
        var integration = new CapturingIntegrationEventPublisher();
        var handler = new UpdateEmployeeProfileHandler(
            ctxA, new FakeClock(FixedUtcNow), new FakeCompanyContactValidationReader(), audit, integration);

        var result = await handler.HandleAsync(
            ProfileRequest(companyId, employee.Id, expectedVersion: 1, firstName: "Loser"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        Assert.Empty(integration.Published);
    }

    [Fact]
    public async Task Employment_Stale_Save_Publishes_No_Audit_Or_Integration_Events()
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

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            await EmploymentHandler(ctxB).HandleAsync(
                EmploymentRequest(companyId, employee.Id, expectedVersion: 1, notes: "winner"), Guid.NewGuid(), CancellationToken.None);
        }

        var audit = new FakeAuditPublisher();
        var integration = new CapturingIntegrationEventPublisher();
        var handler = new UpdateEmploymentDetailsHandler(
            ctxA, new FakeClock(FixedUtcNow), integration, audit, new FakeCompanyEmployeeNumberSettingsReader());

        var result = await handler.HandleAsync(
            EmploymentRequest(companyId, employee.Id, expectedVersion: 1, notes: "loser"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        Assert.Empty(integration.Published);
    }

    [Fact]
    public async Task Contact_Stale_Save_Publishes_No_Audit_Events()
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

        await using (var ctxB = new EmployeesDbContext(Options(dbName)))
        {
            await ProfileHandler(ctxB).HandleAsync(
                ProfileRequest(companyId, employee.Id, expectedVersion: 1, firstName: "AdminEdited"), Guid.NewGuid(), CancellationToken.None);
        }

        var audit = new FakeAuditPublisher();
        var handler = new UpdateMyContactDetailsHandler(
            ctxA, new FakeClock(FixedUtcNow), audit, new FakeCompanyContactValidationReader());

        var result = await handler.HandleAsync(
            ContactRequest(companyId, expectedVersion: 1, city: "StaleCity"), employee.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
    }
}
