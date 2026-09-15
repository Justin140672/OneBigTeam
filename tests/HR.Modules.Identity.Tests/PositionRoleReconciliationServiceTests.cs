using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Ticket 6 follow-up: covers PositionRoleReconciliationService.ReconcileAllCompaniesAsync — the
/// converge-style reconciliation that (unlike the additive-only IAM-03 backfill covered by
/// ReconcilePositionRoleAssignmentsTests.cs) actively expires stale identity.user_positions
/// assignments as well as filling in missing ones. Built via a minimal ServiceProvider mirroring
/// ReconcilePositionRoleAssignmentsTests.BuildServices.
/// </summary>
[Collection("IdentityDatabase")]
public class PositionRoleReconciliationServiceTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Single-company audience reader for reconciliation tests: profiles keyed by employee id,
    /// with an optional per-employee throw to simulate an isolated failure for scenario 5.
    /// </summary>
    private sealed class ReconciliationAudienceReader(
        Guid companyId,
        IReadOnlyDictionary<Guid, EmployeeAudienceProfile> profilesById,
        Guid? throwForEmployeeId = null) : IEmployeeAudienceReader
    {
        public Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(Guid cId, Guid employeeId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyDictionary<Guid, EmployeeAudienceProfile>> GetEmployeeAudienceProfilesAsync(
            Guid cId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
        {
            if (cId != companyId)
                return Task.FromResult((IReadOnlyDictionary<Guid, EmployeeAudienceProfile>)new Dictionary<Guid, EmployeeAudienceProfile>());

            // Profiles are returned as normal (throwForEmployeeId only affects per-employee
            // reconciliation, exercised by making that employee's "current position" resolve to a
            // position id that the FakePositionProfileReader is configured to throw for, or by
            // relying on ReconcileEmployeeAsync's own try/catch around a bad read). Here we simulate
            // the isolated-failure scenario at the profile level: an employee entry whose
            // PositionProfileId is a sentinel the position reader is wired to throw for.
            return Task.FromResult((IReadOnlyDictionary<Guid, EmployeeAudienceProfile>)
                profilesById.Where(kvp => employeeIds.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        }

        public Task<IReadOnlyList<EmployeeAudienceDetail>> GetEmployeeAudienceDetailsAsync(
            Guid cId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) => throw new NotImplementedException();

        public Task<bool> DepartmentExistsAsync(Guid cId, Guid departmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> LocationExistsAsync(Guid cId, Guid locationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> PositionProfileExistsAsync(Guid cId, Guid positionProfileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> EmployeeExistsAsync(Guid cId, Guid employeeId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetDepartmentNameAsync(Guid cId, Guid departmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetLocationNameAsync(Guid cId, Guid locationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetPositionProfileNameAsync(Guid cId, Guid positionProfileId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
            Guid cId, IReadOnlyCollection<Guid> departmentIds, IReadOnlyCollection<Guid> locationIds,
            IReadOnlyCollection<Guid> positionProfileIds, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Guid>> GetAllEmployeeIdsAsync(Guid cId, CancellationToken ct) =>
            Task.FromResult(cId == companyId ? (IReadOnlyList<Guid>)profilesById.Keys.ToList() : []);
    }

    /// <summary>
    /// Multi-company variant, for the per-employee/per-company isolation scenarios.
    /// </summary>
    private sealed class MultiCompanyAudienceReader(
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, EmployeeAudienceProfile>> byCompany) : IEmployeeAudienceReader
    {
        public Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(Guid companyId, Guid employeeId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyDictionary<Guid, EmployeeAudienceProfile>> GetEmployeeAudienceProfilesAsync(
            Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
        {
            IReadOnlyDictionary<Guid, EmployeeAudienceProfile> result = byCompany.TryGetValue(companyId, out var profiles)
                ? profiles.Where(kvp => employeeIds.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                : new Dictionary<Guid, EmployeeAudienceProfile>();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<EmployeeAudienceDetail>> GetEmployeeAudienceDetailsAsync(
            Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) => throw new NotImplementedException();

        public Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> PositionProfileExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> EmployeeExistsAsync(Guid companyId, Guid employeeId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetDepartmentNameAsync(Guid companyId, Guid departmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetLocationNameAsync(Guid companyId, Guid locationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetPositionProfileNameAsync(Guid companyId, Guid positionProfileId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
            Guid companyId, IReadOnlyCollection<Guid> departmentIds, IReadOnlyCollection<Guid> locationIds,
            IReadOnlyCollection<Guid> positionProfileIds, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Guid>> GetAllEmployeeIdsAsync(Guid companyId, CancellationToken ct)
        {
            IReadOnlyList<Guid> result = byCompany.TryGetValue(companyId, out var profiles) ? profiles.Keys.ToList() : [];
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// A position-profile reader that throws for a specific position id, used to simulate a
    /// transient read failure for one employee's position without affecting others (scenario 5).
    /// </summary>
    private sealed class ThrowingPositionProfileReader(
        Guid throwForPositionProfileId,
        IReadOnlyDictionary<Guid, PositionProfileSummary>? summaries) : IPositionProfileReader
    {
        private readonly FakePositionProfileReader _inner = new(summaries: summaries);

        public Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(companyId, positionProfileId, cancellationToken);

        public Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(Guid companyId, Guid? departmentId, string title, CancellationToken cancellationToken) =>
            _inner.FindActiveMatchesAsync(companyId, departmentId, title, cancellationToken);

        public Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            _inner.GetDepartmentIdAsync(companyId, positionProfileId, cancellationToken);

        public Task<PositionProfileSummary?> GetSummaryAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
        {
            if (positionProfileId == throwForPositionProfileId)
                throw new InvalidOperationException("Simulated transient read failure for this position.");
            return _inner.GetSummaryAsync(companyId, positionProfileId, cancellationToken);
        }

        public Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken) =>
            _inner.GetSummariesAsync(companyId, positionProfileIds, cancellationToken);

        public Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
            _inner.GetIdsByDepartmentAsync(companyId, departmentId, cancellationToken);

        public Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            _inner.GetEmploymentDefaultsAsync(companyId, positionProfileId, cancellationToken);

        public Task<IReadOnlyList<Guid>> GetAllActiveIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
            _inner.GetAllActiveIdsAsync(companyId, cancellationToken);

        public Task<IReadOnlyList<Guid>> GetAllIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
            _inner.GetAllIdsAsync(companyId, cancellationToken);
    }

    private PositionRoleReconciliationService BuildService(
        IdentityDbContext db, IEmployeeAudienceReader audienceReader, IPositionProfileReader positionProfileReader)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<PositionRoleReconciliationService>>();
        var positionSync = new PositionSync(db, positionProfileReader);
        // The service must reconcile against the SAME "now" the seeded fixture data (and this
        // test's assertions) use — not the real wall clock — otherwise ExpiresAt gets stamped with
        // the real current time, which is always later than the fixed `Now` these tests assert
        // against, making a just-expired row look "still active" from the test's point of view.
        return new PositionRoleReconciliationService(db, audienceReader, positionSync, new FakeClock(Now.UtcDateTime), logger);
    }

    private async Task SeedUserProfile(Guid companyId, Guid? userId = null)
    {
        await using var db = fixture.BuildContext();
        db.UserProfiles.Add(UserProfile.Create(
            userId ?? Guid.NewGuid(), Guid.NewGuid(), companyId, $"{Guid.NewGuid():N}@test.com", "Test", "User", Now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reconcile_Reopens_Expired_Assignment_For_Current_Position_And_Expires_Stale_Active_One()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var stalePositionId = Guid.NewGuid();
        var currentPositionId = Guid.NewGuid();
        await SeedUserProfile(companyId, employeeId);

        await using (var seed = fixture.BuildContext())
        {
            seed.Positions.Add(Position.Create(stalePositionId, companyId, "Stale Position", Now));
            seed.Positions.Add(Position.Create(currentPositionId, companyId, "Current Position", Now));
            // Stale position is still active (a failed/missed revocation).
            seed.UserPositions.Add(UserPosition.Create(employeeId, stalePositionId, Now.AddDays(-90)));
            // Current position was previously held, then expired.
            seed.UserPositions.Add(UserPosition.Create(employeeId, currentPositionId, Now.AddDays(-200), Now.AddDays(-90)));
            await seed.SaveChangesAsync();
        }

        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, currentPositionId) });
        var positionReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
        {
            [currentPositionId] = new(currentPositionId, "Current Position", null, null, true, null, null),
        });

        await using var db = fixture.BuildContext();
        var service = BuildService(db, audienceReader, positionReader);
        await service.ReconcileAllCompaniesAsync(CancellationToken.None);

        await using var verify = fixture.BuildContext();
        var stale = await verify.UserPositions.SingleAsync(up => up.UserId == employeeId && up.PositionId == stalePositionId);
        Assert.False(stale.IsActive(Now));

        // SingleAsync above already proves there is exactly one row for (employeeId, currentPositionId)
        // — a duplicate-key insert would have thrown a DbUpdateException during reconciliation, and a
        // second surviving row would fail this SingleAsync. Reopened in place, not duplicated.
        var current = await verify.UserPositions.SingleAsync(up => up.UserId == employeeId && up.PositionId == currentPositionId);
        Assert.True(current.IsActive(Now)); // reopened, no duplicate-key failure
        Assert.Equal(1, await verify.UserPositions.CountAsync(up => up.UserId == employeeId && up.PositionId == currentPositionId));
    }

    [Fact]
    public async Task Reconcile_Expires_All_Multiple_Stale_Active_Assignments_Leaving_Exactly_One_Active_For_Current_Position()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var staleA = Guid.NewGuid();
        var staleB = Guid.NewGuid();
        var staleC = Guid.NewGuid();
        var currentPositionId = Guid.NewGuid();
        await SeedUserProfile(companyId, employeeId);

        await using (var seed = fixture.BuildContext())
        {
            foreach (var (id, name) in new[] { (staleA, "A"), (staleB, "B"), (staleC, "C"), (currentPositionId, "Current") })
                seed.Positions.Add(Position.Create(id, companyId, name, Now));

            seed.UserPositions.Add(UserPosition.Create(employeeId, staleA, Now.AddDays(-90)));
            seed.UserPositions.Add(UserPosition.Create(employeeId, staleB, Now.AddDays(-60)));
            seed.UserPositions.Add(UserPosition.Create(employeeId, staleC, Now.AddDays(-30)));
            seed.UserPositions.Add(UserPosition.Create(employeeId, currentPositionId, Now.AddDays(-10)));
            await seed.SaveChangesAsync();
        }

        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, currentPositionId) });
        var positionReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
        {
            [currentPositionId] = new(currentPositionId, "Current", null, null, true, null, null),
        });

        await using var db = fixture.BuildContext();
        var service = BuildService(db, audienceReader, positionReader);
        await service.ReconcileAllCompaniesAsync(CancellationToken.None);

        await using var verify = fixture.BuildContext();
        var assignments = await verify.UserPositions.Where(up => up.UserId == employeeId).ToListAsync();
        var active = assignments.Where(a => a.IsActive(Now)).ToList();
        Assert.Single(active);
        Assert.Equal(currentPositionId, active[0].PositionId);
        Assert.Equal(3, assignments.Count(a => !a.IsActive(Now)));
    }

    [Fact]
    public async Task Reconcile_Employee_With_No_Current_Position_Expires_Every_Previously_Active_Assignment()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var formerPositionId = Guid.NewGuid();
        await SeedUserProfile(companyId, employeeId);

        await using (var seed = fixture.BuildContext())
        {
            seed.Positions.Add(Position.Create(formerPositionId, companyId, "Former Position", Now));
            seed.UserPositions.Add(UserPosition.Create(employeeId, formerPositionId, Now.AddDays(-30)));
            await seed.SaveChangesAsync();
        }

        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, null) });
        var positionReader = new FakePositionProfileReader();

        await using var db = fixture.BuildContext();
        var service = BuildService(db, audienceReader, positionReader);
        await service.ReconcileAllCompaniesAsync(CancellationToken.None);

        await using var verify = fixture.BuildContext();
        var assignments = await verify.UserPositions.Where(up => up.UserId == employeeId).ToListAsync();
        Assert.Single(assignments); // no new row created
        Assert.False(assignments[0].IsActive(Now));
    }

    [Fact]
    public async Task Reconcile_Expires_Stale_Assignment_When_Confirmed_New_Position_Cannot_Yet_Be_Resolved()
    {
        // P1 follow-up: an unresolved NEW position must not justify retaining grants for the OLD
        // position the authoritative employee data confirms the employee no longer holds.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var unresolvablePositionId = Guid.NewGuid();
        var staleActivePositionId = Guid.NewGuid();
        await SeedUserProfile(companyId, employeeId);

        await using (var seed = fixture.BuildContext())
        {
            seed.Positions.Add(Position.Create(staleActivePositionId, companyId, "Stale Position", Now));
            seed.UserPositions.Add(UserPosition.Create(employeeId, staleActivePositionId, Now.AddDays(-10)));
            await seed.SaveChangesAsync();
        }

        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, unresolvablePositionId) });
        // No summary registered for unresolvablePositionId -> GetSummaryAsync returns null -> PositionSync.EnsureExistsAsync returns null.
        var positionReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>());

        await using var db = fixture.BuildContext();
        var service = BuildService(db, audienceReader, positionReader);
        await service.ReconcileAllCompaniesAsync(CancellationToken.None);

        await using var verify = fixture.BuildContext();
        var assignments = await verify.UserPositions.Where(up => up.UserId == employeeId).ToListAsync();
        Assert.Single(assignments); // no new row created for the unresolvable position
        Assert.Equal(staleActivePositionId, assignments[0].PositionId);
        Assert.False(assignments[0].IsActive(Now)); // expired despite the new position being unresolvable
        Assert.False(await verify.Positions.AnyAsync(p => p.Id == unresolvablePositionId)); // never created
    }

    [Fact]
    public async Task Reconcile_Later_Resolves_New_Position_Without_Duplicating_The_Already_Expired_Stale_Row()
    {
        // Two-pass scenario: pass 1 confirms B but can't resolve it, so A is expired and B stays
        // unprovisioned; pass 2 resolves B and must create exactly one assignment for it, with A
        // still expired (not resurrected) and no duplicate-key failure.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldPositionId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        await SeedUserProfile(companyId, employeeId);

        await using (var seed = fixture.BuildContext())
        {
            seed.Positions.Add(Position.Create(oldPositionId, companyId, "Old Position", Now));
            seed.UserPositions.Add(UserPosition.Create(employeeId, oldPositionId, Now.AddDays(-10)));
            await seed.SaveChangesAsync();
        }

        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, newPositionId) });

        await using (var db1 = fixture.BuildContext())
        {
            var unresolvedReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>());
            var service1 = BuildService(db1, audienceReader, unresolvedReader);
            await service1.ReconcileAllCompaniesAsync(CancellationToken.None);
        }

        await using (var verifyPass1 = fixture.BuildContext())
        {
            var old = await verifyPass1.UserPositions.SingleAsync(up => up.UserId == employeeId && up.PositionId == oldPositionId);
            Assert.False(old.IsActive(Now));
            Assert.False(await verifyPass1.UserPositions.AnyAsync(up => up.UserId == employeeId && up.PositionId == newPositionId));
        }

        await using (var db2 = fixture.BuildContext())
        {
            var resolvedReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
            {
                [newPositionId] = new(newPositionId, "New Position", null, null, true, null, null),
            });
            var service2 = BuildService(db2, audienceReader, resolvedReader);
            await service2.ReconcileAllCompaniesAsync(CancellationToken.None);
        }

        await using var verify = fixture.BuildContext();
        var oldFinal = await verify.UserPositions.SingleAsync(up => up.UserId == employeeId && up.PositionId == oldPositionId);
        Assert.False(oldFinal.IsActive(Now)); // still expired, not resurrected

        var newAssignments = await verify.UserPositions.Where(up => up.UserId == employeeId && up.PositionId == newPositionId).ToListAsync();
        Assert.Single(newAssignments); // exactly one row, no duplicate-key failure
        Assert.True(newAssignments[0].IsActive(Now));
    }

    [Fact]
    public async Task Reconcile_Expires_Stale_Assignment_When_New_Position_Lookup_Throws_After_Authoritative_Position_Confirmed()
    {
        // P1 follow-up: EnsureExistsAsync throwing (e.g. a transient PositionProfile read failure)
        // must be isolated from the outer per-employee catch in ReconcileAllCompaniesAsync, exactly
        // like a resolved-to-null new position is (see
        // Reconcile_Expires_Stale_Assignment_When_Confirmed_New_Position_Cannot_Yet_Be_Resolved) — a
        // throw must not skip the whole employee before A's stale assignment is expired.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var stalePositionId = Guid.NewGuid();
        var throwingPositionId = Guid.NewGuid();
        await SeedUserProfile(companyId, employeeId);

        await using (var seed = fixture.BuildContext())
        {
            seed.Positions.Add(Position.Create(stalePositionId, companyId, "Stale Position", Now));
            seed.UserPositions.Add(UserPosition.Create(employeeId, stalePositionId, Now.AddDays(-10)));
            await seed.SaveChangesAsync();
        }

        // Authoritative employee data already confirms the employee is now on throwingPositionId.
        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, throwingPositionId) });
        // But resolving/provisioning it throws instead of returning null.
        var positionReader = new ThrowingPositionProfileReader(throwingPositionId, summaries: null);

        await using (var db = fixture.BuildContext())
        {
            var service = BuildService(db, audienceReader, positionReader);
            await service.ReconcileAllCompaniesAsync(CancellationToken.None);
        }

        await using (var verify = fixture.BuildContext())
        {
            var assignments = await verify.UserPositions.Where(up => up.UserId == employeeId).ToListAsync();
            var stale = Assert.Single(assignments); // no new row created for the throwing position
            Assert.Equal(stalePositionId, stale.PositionId);
            Assert.False(stale.IsActive(Now)); // expired despite the new position lookup throwing
            Assert.False(await verify.Positions.AnyAsync(p => p.Id == throwingPositionId)); // never created
        }

        // A later pass, once the position becomes resolvable, provisions it safely — no duplicate-key
        // failure from the still-present expired stale row, and no leftover exception state.
        await using (var db = fixture.BuildContext())
        {
            var resolvedReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
            {
                [throwingPositionId] = new(throwingPositionId, "Resolved Position", null, null, true, null, null),
            });
            var service = BuildService(db, audienceReader, resolvedReader);
            await service.ReconcileAllCompaniesAsync(CancellationToken.None);
        }

        await using var final = fixture.BuildContext();
        var staleFinal = await final.UserPositions.SingleAsync(up => up.UserId == employeeId && up.PositionId == stalePositionId);
        Assert.False(staleFinal.IsActive(Now)); // still expired, not resurrected

        var resolvedAssignments = await final.UserPositions.Where(up => up.UserId == employeeId && up.PositionId == throwingPositionId).ToListAsync();
        Assert.Single(resolvedAssignments); // exactly one row, no duplicate-key failure
        Assert.True(resolvedAssignments[0].IsActive(Now));
    }

    [Fact]
    public async Task Reconcile_Preserves_Already_Active_Current_Assignment_When_Its_Own_Profile_Lookup_Throws()
    {
        // P2 follow-up: classification of "is this assignment current" must use the authoritative
        // currentPositionProfileId, not the separately-resolved `currentPosition` (which is null
        // whenever EnsureExistsAsync throws) — otherwise an already-active, still-correct assignment
        // for the confirmed current position gets wrongly expired alongside genuinely stale ones.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var stalePositionId = Guid.NewGuid();
        var currentPositionId = Guid.NewGuid();
        var directRoleId = SystemRoles.Employee;
        await SeedUserProfile(companyId, employeeId);

        await using (var seed = fixture.BuildContext())
        {
            seed.Positions.Add(Position.Create(stalePositionId, companyId, "Stale Position", Now));
            seed.Positions.Add(Position.Create(currentPositionId, companyId, "Current Position", Now));
            seed.UserPositions.Add(UserPosition.Create(employeeId, stalePositionId, Now.AddDays(-30)));
            // Current position's assignment already exists and is active from an earlier, successful sync.
            seed.UserPositions.Add(UserPosition.Create(employeeId, currentPositionId, Now.AddDays(-10)));
            seed.UserRoles.Add(UserRole.Create(employeeId, directRoleId, Now));
            await seed.SaveChangesAsync();
        }

        // Authoritative employee data confirms current position B, but resolving/refreshing B's
        // profile throws this pass (e.g. a transient read failure) instead of returning a summary.
        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, currentPositionId) });
        var throwingReader = new ThrowingPositionProfileReader(currentPositionId, summaries: null);

        // Run twice to prove repeated lookup failures neither revoke B nor restore A.
        for (var pass = 0; pass < 2; pass++)
        {
            await using var db = fixture.BuildContext();
            var service = BuildService(db, audienceReader, throwingReader);
            await service.ReconcileAllCompaniesAsync(CancellationToken.None);
        }

        await using (var verify = fixture.BuildContext())
        {
            var stale = await verify.UserPositions.SingleAsync(up => up.UserId == employeeId && up.PositionId == stalePositionId);
            Assert.False(stale.IsActive(Now)); // stale A still expired

            var current = await verify.UserPositions.SingleAsync(up => up.UserId == employeeId && up.PositionId == currentPositionId);
            Assert.True(current.IsActive(Now)); // active B preserved despite its own lookup throwing

            // Direct roles/overrides untouched by reconciliation.
            Assert.True(await verify.UserRoles.AnyAsync(ur => ur.UserId == employeeId && ur.RoleId == directRoleId));
        }

        // A later successful lookup must not duplicate the already-active B assignment.
        await using (var db = fixture.BuildContext())
        {
            var resolvedReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
            {
                [currentPositionId] = new(currentPositionId, "Current Position", null, null, true, null, null),
            });
            var service = BuildService(db, audienceReader, resolvedReader);
            await service.ReconcileAllCompaniesAsync(CancellationToken.None);
        }

        await using var final = fixture.BuildContext();
        var currentAssignments = await final.UserPositions.Where(up => up.UserId == employeeId && up.PositionId == currentPositionId).ToListAsync();
        Assert.Single(currentAssignments); // no duplicate row
        Assert.True(currentAssignments[0].IsActive(Now));
    }

    [Fact]
    public async Task Reconcile_Isolates_One_Employees_Failure_So_Others_In_Same_And_Other_Companies_Still_Converge()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var failingEmployeeId = Guid.NewGuid();
        var healthyEmployeeId = Guid.NewGuid();
        var otherCompanyEmployeeId = Guid.NewGuid();

        var failingPositionId = Guid.NewGuid(); // resolving this position throws
        var healthyPositionId = Guid.NewGuid();
        var otherCompanyPositionId = Guid.NewGuid();

        await SeedUserProfile(companyId, failingEmployeeId);
        await SeedUserProfile(companyId, healthyEmployeeId);
        await SeedUserProfile(otherCompanyId, otherCompanyEmployeeId);

        var byCompany = new Dictionary<Guid, IReadOnlyDictionary<Guid, EmployeeAudienceProfile>>
        {
            [companyId] = new Dictionary<Guid, EmployeeAudienceProfile>
            {
                [failingEmployeeId] = new(null, null, failingPositionId),
                [healthyEmployeeId] = new(null, null, healthyPositionId),
            },
            [otherCompanyId] = new Dictionary<Guid, EmployeeAudienceProfile>
            {
                [otherCompanyEmployeeId] = new(null, null, otherCompanyPositionId),
            },
        };
        var audienceReader = new MultiCompanyAudienceReader(byCompany);

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [healthyPositionId] = new(healthyPositionId, "Healthy Position", null, null, true, null, null),
            [otherCompanyPositionId] = new(otherCompanyPositionId, "Other Company Position", null, null, true, null, null),
        };
        var positionReader = new ThrowingPositionProfileReader(failingPositionId, summaries);

        await using var db = fixture.BuildContext();
        var service = BuildService(db, audienceReader, positionReader);
        // Must not throw out of the whole run despite the failing employee's position resolution blowing up.
        await service.ReconcileAllCompaniesAsync(CancellationToken.None);

        await using var verify = fixture.BuildContext();
        Assert.True(await verify.UserPositions.AnyAsync(up => up.UserId == healthyEmployeeId && up.PositionId == healthyPositionId));
        Assert.True(await verify.UserPositions.AnyAsync(up => up.UserId == otherCompanyEmployeeId && up.PositionId == otherCompanyPositionId));
        // The failing employee got no assignment created for the unresolvable position (isolated failure, not silently ignored-successfully),
        // but also has no pre-existing assignment here to expire — this scenario is about failure isolation, not revocation.
        Assert.False(await verify.UserPositions.AnyAsync(up => up.UserId == failingEmployeeId && up.PositionId == failingPositionId));
    }

    [Fact]
    public async Task Reconcile_Never_Touches_Direct_UserRoles_Or_EmployeeRoleOverrides()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var stalePositionId = Guid.NewGuid();
        var currentPositionId = Guid.NewGuid();
        var directRoleId = SystemRoles.Employee;
        await SeedUserProfile(companyId, employeeId);

        Guid overrideId;
        await using (var seed = fixture.BuildContext())
        {
            seed.Positions.Add(Position.Create(stalePositionId, companyId, "Stale Position", Now));
            seed.Positions.Add(Position.Create(currentPositionId, companyId, "Current Position", Now));
            seed.UserPositions.Add(UserPosition.Create(employeeId, stalePositionId, Now.AddDays(-30)));
            seed.UserRoles.Add(UserRole.Create(employeeId, directRoleId, Now));
            var @override = EmployeeRoleOverride.Create(
                companyId, employeeId, SystemRoles.Manager, EmployeeRoleOverrideType.Grant, "Test override", null, Now);
            overrideId = @override.Id;
            seed.EmployeeRoleOverrides.Add(@override);
            await seed.SaveChangesAsync();
        }

        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, currentPositionId) });
        var positionReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
        {
            [currentPositionId] = new(currentPositionId, "Current Position", null, null, true, null, null),
        });

        await using var db = fixture.BuildContext();
        var service = BuildService(db, audienceReader, positionReader);
        await service.ReconcileAllCompaniesAsync(CancellationToken.None);

        await using var verify = fixture.BuildContext();
        Assert.True(await verify.UserRoles.AnyAsync(ur => ur.UserId == employeeId && ur.RoleId == directRoleId));
        Assert.True(await verify.EmployeeRoleOverrides.AnyAsync(o => o.Id == overrideId));

        // Position reconciliation still happened as expected alongside the untouched direct grants.
        var stale = await verify.UserPositions.SingleAsync(up => up.UserId == employeeId && up.PositionId == stalePositionId);
        Assert.False(stale.IsActive(Now));
        Assert.True(await verify.UserPositions.AnyAsync(up => up.UserId == employeeId && up.PositionId == currentPositionId && up.ExpiresAt == null));
    }

    [Fact]
    public async Task Reconcile_Converges_Preexisting_Inconsistent_State_In_One_Pass()
    {
        // Simulates data left behind by historical bugs/duplicated events: two stale active
        // assignments AND an already-expired row for what is now the current position, all at once.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var staleA = Guid.NewGuid();
        var staleB = Guid.NewGuid();
        var currentPositionId = Guid.NewGuid();
        await SeedUserProfile(companyId, employeeId);

        await using (var seed = fixture.BuildContext())
        {
            foreach (var (id, name) in new[] { (staleA, "A"), (staleB, "B"), (currentPositionId, "Current") })
                seed.Positions.Add(Position.Create(id, companyId, name, Now));

            seed.UserPositions.Add(UserPosition.Create(employeeId, staleA, Now.AddDays(-100)));
            seed.UserPositions.Add(UserPosition.Create(employeeId, staleB, Now.AddDays(-50)));
            seed.UserPositions.Add(UserPosition.Create(employeeId, currentPositionId, Now.AddDays(-200), Now.AddDays(-100)));
            await seed.SaveChangesAsync();
        }

        var audienceReader = new ReconciliationAudienceReader(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, currentPositionId) });
        var positionReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
        {
            [currentPositionId] = new(currentPositionId, "Current", null, null, true, null, null),
        });

        await using var db = fixture.BuildContext();
        var service = BuildService(db, audienceReader, positionReader);
        await service.ReconcileAllCompaniesAsync(CancellationToken.None);

        await using var verify = fixture.BuildContext();
        var assignments = await verify.UserPositions.Where(up => up.UserId == employeeId).ToListAsync();
        var active = assignments.Where(a => a.IsActive(Now)).ToList();
        Assert.Single(active);
        Assert.Equal(currentPositionId, active[0].PositionId);
        Assert.Equal(2, assignments.Count(a => !a.IsActive(Now)));
    }
}
