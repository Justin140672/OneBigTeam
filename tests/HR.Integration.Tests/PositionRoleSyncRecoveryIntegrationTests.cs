using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 6 follow-up (P1): end-to-end, real-Postgres coverage for position-role permission-sync
/// reliability across module boundaries (HR.Modules.Employees -> outbox -> HR.Modules.Identity).
/// Position transfers are applied directly against the real <see cref="Employee"/> aggregate/
/// EmployeesDbContext (Employee.Assign + the same outbox-staging pattern used by
/// UpdateEmployeeProfileAndEmploymentHandler) rather than guessing the exact HTTP request contract
/// of the employment-update endpoints — this still exercises the real cross-module read
/// (IEmployeeAudienceReader backed by EmployeesDbContext), the real outbox, the real
/// OnEmployeePositionChanged handler and the real PositionRoleReconciliationService against a real
/// Postgres database, which is what this ticket's guarantees are actually about. Effective access
/// is asserted both via direct identity.user_positions row state and via the real
/// GET .../effective-access endpoint (IdentityAuthorizationService.GetEffectiveRolesAsync).
/// </summary>
[Collection("Integration")]
public class PositionRoleSyncRecoveryIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public PositionRoleSyncRecoveryIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Creates a second, distinct PositionProfile in the same company/department/location as
    /// an already-seeded <see cref="EmployeeReferenceDataSeeder.ReferenceData"/>, so a test can
    /// transfer an employee between two real position profiles.</summary>
    private async Task<Guid> SeedAdditionalPositionProfileAsync(
        Guid companyId, EmployeeReferenceDataSeeder.ReferenceData referenceData, string title)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var now = DateTimeOffset.UtcNow;

        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, referenceData.DepartmentId, referenceData.LocationId, title, null,
            probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
            salaryMin: null, salaryMax: null, salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), now);

        db.PositionProfiles.Add(positionProfile);
        await db.SaveChangesAsync();
        return positionProfile.Id;
    }

    /// <summary>
    /// Registers a role as a default for a position via the real identity.position_roles table
    /// (bypassing PositionSync's lazy identity.positions projection — the position row is created
    /// here directly since these tests never go through the SetPositionRoleDefaults endpoint).
    /// </summary>
    private async Task<Guid> SeedPositionWithDefaultRoleAsync(Guid companyId, Guid positionProfileId, string positionName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        var roleId = Guid.NewGuid();
        db.Positions.Add(Position.Create(positionProfileId, companyId, positionName, now));
        db.Roles.Add(Role.Create(roleId, $"Role-{Guid.NewGuid():N}", now));
        db.PositionRoles.Add(PositionRole.Create(positionProfileId, roleId, now));
        await db.SaveChangesAsync();
        return roleId;
    }

    /// <summary>
    /// Applies a position transfer directly to the real Employee aggregate, mirroring
    /// UpdateEmployeeProfileAndEmploymentHandler's own outbox-staging pattern (Ticket 6 follow-up):
    /// the EmployeePositionChangedIntegrationEvent is enqueued in the SAME SaveChangesAsync as the
    /// employee write. Pass <paramref name="dispatchOutbox"/>=false to simulate "the process crashed
    /// right after commit, before the outbox was ever dispatched" — the row exists, but the event
    /// was never delivered to Identity.
    /// </summary>
    private async Task<Guid> TransferEmployeePositionAsync(
        Guid companyId, Guid employeeId, Guid newPositionProfileId, bool dispatchOutbox = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var now = DateTimeOffset.UtcNow;

        var employee = await db.Employees.SingleAsync(e => e.Id == employeeId && e.CompanyId == companyId);
        var previousPositionId = employee.PositionProfileId;

        employee.Assign(employee.DepartmentId, newPositionProfileId, employee.LocationId, employee.ManagerId, now);

        db.AuditOutboxEntries.EnqueueIntegrationOutbox(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, previousPositionId, newPositionProfileId, now),
            companyId, now);

        await db.SaveChangesAsync();

        if (dispatchOutbox)
        {
            var auditPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
            var integrationPublisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<PositionRoleSyncRecoveryIntegrationTests>>();

            await db.DispatchPendingAsync(
                db.AuditOutboxEntries, auditPublisher, now, IdempotencyCleanupExtensions.DefaultBatchSize,
                logger, CancellationToken.None, integrationPublisher);
        }

        return previousPositionId;
    }

    private async Task RunReconciliationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.ReconcilePositionRoleAssignmentsAsync();
    }

    private async Task<HashSet<Guid>> GetEffectiveRolesAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var roles = await authService.GetEffectiveRolesAsync(userId, CancellationToken.None);
        return roles.ToHashSet();
    }

    private async Task<List<UserPosition>> GetUserPositionsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.UserPositions.Where(up => up.UserId == userId).ToListAsync();
    }

    /// <summary>Seeds a real Employee + a real matching ApplicationUser/UserProfile (system-access
    /// user) so both the Employees-side reads and Identity-side role lookups line up on the same
    /// id, as production requires (UserPosition.UserId == the employee's own id).</summary>
    private async Task<(Guid EmployeeId, EmployeeReferenceDataSeeder.ReferenceData ReferenceData)> SeedEmployeeWithUserAsync(
        Guid companyId, Guid initialPositionProfileId)
    {
        using var scope = _factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(employeesDb, companyId);

        var employeeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var employee = Employee.Create(
            employeeId, companyId, "Sync", "Recovery",
            workEmail: $"sync.recovery.{Guid.NewGuid():N}@test.example",
            startDate: new DateOnly(2026, 1, 1),
            hasSystemAccess: true,
            dateOfBirth: new DateOnly(1990, 1, 1),
            nationality: "British",
            gender: "Prefer not to say",
            employeeNumber: $"EMP-{Guid.NewGuid():N}",
            employmentTypeId: referenceData.EmploymentTypeId,
            departmentId: referenceData.DepartmentId,
            locationId: referenceData.LocationId,
            positionProfileId: initialPositionProfileId,
            now: now);
        employeesDb.Employees.Add(employee);
        await employeesDb.SaveChangesAsync();

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        identityDb.Users.Add(ApplicationUser.Create(employeeId, employee.WorkEmail, "not-used-in-tests", "Sync", "Recovery", now));
        identityDb.UserProfiles.Add(UserProfile.Create(employeeId, Guid.NewGuid(), companyId, employee.WorkEmail, "Sync", "Recovery", now));
        await identityDb.SaveChangesAsync();

        return (employeeId, referenceData);
    }

    [Fact]
    public async Task CrashRecovery_ReconciliationConvergesEmployee_WithoutTheMissedEvent_WhenOutboxWasNeverDispatched()
    {
        // Simulates "process interrupted after employee commit, before publication": the employee
        // write + outbox row exist (TransferEmployeePositionAsync's own SaveChangesAsync), but
        // dispatchOutbox:false means the event is never delivered to Identity — matching exactly
        // what a crash right after commit leaves behind. PositionRoleReconciliationService must
        // independently converge the employee's access to the new position without ever needing
        // that missed event.
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var newPositionProfileId = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Recovery Target Role");
        var newRoleId = await SeedPositionWithDefaultRoleAsync(companyId, newPositionProfileId, "Recovery Target Role");

        var (employeeId, _) = await SeedEmployeeWithUserAsync(companyId, referenceData.PositionProfileId);

        await TransferEmployeePositionAsync(companyId, employeeId, newPositionProfileId, dispatchOutbox: false);

        // Sanity: no user_positions row for the new position yet — the event was never delivered.
        var beforeReconcile = await GetUserPositionsAsync(employeeId);
        Assert.DoesNotContain(beforeReconcile, up => up.PositionId == newPositionProfileId);

        await RunReconciliationAsync();

        var effectiveRoles = await GetEffectiveRolesAsync(employeeId);
        Assert.Contains(newRoleId, effectiveRoles);

        var afterReconcile = await GetUserPositionsAsync(employeeId);
        var active = afterReconcile.Where(up => up.IsActive(DateTimeOffset.UtcNow)).ToList();
        Assert.Single(active);
        Assert.Equal(newPositionProfileId, active[0].PositionId);
    }

    [Fact]
    public async Task DuplicateDelivery_ReversedOrder_ConvergesToFinalPosition_NeverReopeningIntermediateGrants()
    {
        // A -> B -> C transfer where the B->C event is processed first (its delivery raced ahead),
        // then a delayed/duplicated A->B event arrives after the employee's real current position
        // is already C. The stale A->B event must be a complete no-op (both directions: it must not
        // reopen B's now-superseded grant, and must not touch C's still-current grant either).
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var positionB = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Position B");
        var positionC = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Position C");
        var roleB = await SeedPositionWithDefaultRoleAsync(companyId, positionB, "Position B");
        var roleC = await SeedPositionWithDefaultRoleAsync(companyId, positionC, "Position C");

        var (employeeId, _) = await SeedEmployeeWithUserAsync(companyId, referenceData.PositionProfileId);
        var positionA = referenceData.PositionProfileId;

        // Real, current transfer A -> C (this is what "actually happened": B was skipped/collapsed
        // by the time events are processed, matching the ticket's own worked example).
        await TransferEmployeePositionAsync(companyId, employeeId, positionC, dispatchOutbox: true);

        var effectiveRolesAfterRealTransfer = await GetEffectiveRolesAsync(employeeId);
        Assert.Contains(roleC, effectiveRolesAfterRealTransfer);
        Assert.DoesNotContain(roleB, effectiveRolesAfterRealTransfer);

        // Now a stale, delayed A -> B event arrives (out-of-order/duplicated delivery) — the
        // employee's current position is already C, not B, so this must be skipped entirely.
        using (var scope = _factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<EmployeePositionChangedIntegrationEvent>>();
            await handler.HandleAsync(
                new EmployeePositionChangedIntegrationEvent(companyId, employeeId, positionA, positionB, DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        var effectiveRolesAfterStaleEvent = await GetEffectiveRolesAsync(employeeId);
        Assert.Contains(roleC, effectiveRolesAfterStaleEvent); // still C
        Assert.DoesNotContain(roleB, effectiveRolesAfterStaleEvent); // B never reopened

        var positions = await GetUserPositionsAsync(employeeId);
        Assert.DoesNotContain(positions, up => up.PositionId == positionB); // B was never created at all
        var active = positions.Where(up => up.IsActive(DateTimeOffset.UtcNow)).ToList();
        Assert.Single(active);
        Assert.Equal(positionC, active[0].PositionId);
    }

    [Fact]
    public async Task ExistingExpiredAssignment_ForCurrentPosition_IsReopened_WithoutDuplicateKeyFailure()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var currentPositionId = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Boomerang Role");
        var roleId = await SeedPositionWithDefaultRoleAsync(companyId, currentPositionId, "Boomerang Role");

        var (employeeId, _) = await SeedEmployeeWithUserAsync(companyId, currentPositionId);

        // Pre-seed an EXPIRED assignment for the employee's current position, simulating "held this
        // position before, left, and has now returned to it" while the row was never cleaned up.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.UserPositions.Add(UserPosition.Create(
                employeeId, currentPositionId, DateTimeOffset.UtcNow.AddDays(-200), DateTimeOffset.UtcNow.AddDays(-100)));
            await db.SaveChangesAsync();
        }

        await RunReconciliationAsync(); // must not throw a duplicate-key DbUpdateException

        var positions = await GetUserPositionsAsync(employeeId);
        var forCurrentPosition = positions.Where(up => up.PositionId == currentPositionId).ToList();
        Assert.Single(forCurrentPosition); // reopened in place, not duplicated
        Assert.True(forCurrentPosition[0].IsActive(DateTimeOffset.UtcNow));

        var effectiveRoles = await GetEffectiveRolesAsync(employeeId);
        Assert.Contains(roleId, effectiveRoles);
    }

    [Fact]
    public async Task MultipleStaleActiveAssignments_ConvergeToExactlyOneActive_EndToEnd()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var staleA = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Stale A");
        var staleB = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Stale B");
        var currentPositionId = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Current");
        var currentRoleId = await SeedPositionWithDefaultRoleAsync(companyId, currentPositionId, "Current");

        var (employeeId, _) = await SeedEmployeeWithUserAsync(companyId, currentPositionId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.Positions.Add(Position.Create(staleA, companyId, "Stale A", now));
            db.Positions.Add(Position.Create(staleB, companyId, "Stale B", now));
            db.UserPositions.Add(UserPosition.Create(employeeId, staleA, now.AddDays(-90)));
            db.UserPositions.Add(UserPosition.Create(employeeId, staleB, now.AddDays(-60)));
            await db.SaveChangesAsync();
        }

        await RunReconciliationAsync();

        var positions = await GetUserPositionsAsync(employeeId);
        var active = positions.Where(up => up.IsActive(DateTimeOffset.UtcNow)).ToList();
        Assert.Single(active);
        Assert.Equal(currentPositionId, active[0].PositionId);

        var effectiveRoles = await GetEffectiveRolesAsync(employeeId);
        Assert.Contains(currentRoleId, effectiveRoles);
    }

    [Fact]
    public async Task RemovingEmployeesPosition_ExpiresStaleGrant_EndToEnd_EffectivePermissionsNoLongerIncludeIt()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var positionId = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Soon Vacant Role");
        var roleId = await SeedPositionWithDefaultRoleAsync(companyId, positionId, "Soon Vacant Role");

        var (employeeId, _) = await SeedEmployeeWithUserAsync(companyId, positionId);

        // Grant the position's role via the handler path (simulating the original transfer INTO
        // the position having already synced correctly).
        using (var scope = _factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<EmployeePositionChangedIntegrationEvent>>();
            await handler.HandleAsync(
                new EmployeePositionChangedIntegrationEvent(companyId, employeeId, Guid.Empty, positionId, DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        Assert.Contains(roleId, await GetEffectiveRolesAsync(employeeId));

        // Employee's position is removed (set to none) directly on the aggregate — Employee.Assign
        // requires a non-null positionProfileId in this codebase's current domain model, so we model
        // "no position" the way IEmployeeAudienceReader actually reports it: reconciliation reads
        // the employee's CURRENT authoritative position via GetEmployeeAudienceProfilesAsync, so we
        // exercise the "no current position" branch directly against the reconciliation service
        // using a null-position override, matching PositionRoleReconciliationServiceTests' unit
        // coverage of the same branch, but here against the real end-to-end user_positions/roles
        // state this test already built up.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<PositionRoleReconciliationService>>();
            var positionSync = scope.ServiceProvider.GetRequiredService<PositionSync>();
            var clock = scope.ServiceProvider.GetRequiredService<HR.SharedKernel.IClock>();
            var audienceReader = new NoPositionAudienceReader(companyId, employeeId);
            var service = new PositionRoleReconciliationService(db, audienceReader, positionSync, clock, logger);
            await service.ReconcileAllCompaniesAsync(CancellationToken.None);
        }

        var effectiveRolesAfter = await GetEffectiveRolesAsync(employeeId);
        Assert.DoesNotContain(roleId, effectiveRolesAfter);

        var positions = await GetUserPositionsAsync(employeeId);
        Assert.DoesNotContain(positions, up => up.IsActive(DateTimeOffset.UtcNow));
    }

    /// <summary>Reports exactly one employee (the one under test) with no current position, and
    /// nothing else — used by the "employee's position removed" scenario above to exercise
    /// PositionRoleReconciliationService's real "no current position" branch end-to-end without
    /// depending on unverified HTTP contract details for "remove an employee's position".</summary>
    private sealed class NoPositionAudienceReader(Guid companyId, Guid employeeId) : IEmployeeAudienceReader
    {
        public Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(Guid cId, Guid eId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyDictionary<Guid, EmployeeAudienceProfile>> GetEmployeeAudienceProfilesAsync(
            Guid cId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) =>
            Task.FromResult(cId == companyId
                ? (IReadOnlyDictionary<Guid, EmployeeAudienceProfile>)new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, null) }
                : new Dictionary<Guid, EmployeeAudienceProfile>());

        public Task<IReadOnlyList<EmployeeAudienceDetail>> GetEmployeeAudienceDetailsAsync(Guid cId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<bool> DepartmentExistsAsync(Guid cId, Guid departmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> LocationExistsAsync(Guid cId, Guid locationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> PositionProfileExistsAsync(Guid cId, Guid positionProfileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> EmployeeExistsAsync(Guid cId, Guid eId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetDepartmentNameAsync(Guid cId, Guid departmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetLocationNameAsync(Guid cId, Guid locationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetPositionProfileNameAsync(Guid cId, Guid positionProfileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(Guid cId, IReadOnlyCollection<Guid> departmentIds, IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<Guid> positionProfileIds, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> GetAllEmployeeIdsAsync(Guid cId, CancellationToken ct) =>
            Task.FromResult(cId == companyId ? (IReadOnlyList<Guid>)[employeeId] : []);
    }

    [Fact]
    public async Task DirectRoleOverride_SurvivesPositionChange_AndReconciliation_EndToEnd()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var oldPositionId = referenceData.PositionProfileId;
        var newPositionId = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "New Role");
        var oldRoleId = await SeedPositionWithDefaultRoleAsync(companyId, oldPositionId, "Old Role");
        var newRoleId = await SeedPositionWithDefaultRoleAsync(companyId, newPositionId, "New Role");

        var (employeeId, _) = await SeedEmployeeWithUserAsync(companyId, oldPositionId);

        // Establish the initial position grant.
        using (var scope = _factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<EmployeePositionChangedIntegrationEvent>>();
            await handler.HandleAsync(
                new EmployeePositionChangedIntegrationEvent(companyId, employeeId, Guid.Empty, oldPositionId, DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        var overrideRoleId = SystemRoles.Manager;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.EmployeeRoleOverrides.Add(EmployeeRoleOverride.Create(
                companyId, employeeId, overrideRoleId, EmployeeRoleOverrideType.Grant, "Covering an audit", null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        Assert.Contains(overrideRoleId, await GetEffectiveRolesAsync(employeeId));

        // Real end-to-end position transfer + reconciliation pass.
        await TransferEmployeePositionAsync(companyId, employeeId, newPositionId, dispatchOutbox: true);
        await RunReconciliationAsync();

        var effectiveRoles = await GetEffectiveRolesAsync(employeeId);
        Assert.Contains(newRoleId, effectiveRoles); // position-derived access updated
        Assert.DoesNotContain(oldRoleId, effectiveRoles); // old position's grant gone
        Assert.Contains(overrideRoleId, effectiveRoles); // override untouched by either the transfer or reconciliation
    }

    [Fact]
    public async Task ConcurrentTransferAndReconciliation_NeverCrashesWithUnhandledDuplicateKeyFailure_AndConvergesOnRetry()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var newPositionId = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Contested Role");
        var newRoleId = await SeedPositionWithDefaultRoleAsync(companyId, newPositionId, "Contested Role");

        var (employeeId, _) = await SeedEmployeeWithUserAsync(companyId, referenceData.PositionProfileId);

        // Stage (but do not yet dispatch) the transfer so the Handler and the reconciliation pass
        // race against the same underlying rows for the same employee.
        var previousPositionId = await TransferEmployeePositionAsync(companyId, employeeId, newPositionId, dispatchOutbox: false);

        using var handlerScope = _factory.Services.CreateScope();
        using var reconcileScope = _factory.Services.CreateScope();
        var handler = handlerScope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<EmployeePositionChangedIntegrationEvent>>();
        var reconciliationService = reconcileScope.ServiceProvider.GetRequiredService<PositionRoleReconciliationService>();

        var handlerTask = handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, previousPositionId, newPositionId, DateTimeOffset.UtcNow),
            CancellationToken.None);
        var reconcileTask = reconciliationService.ReconcileAllCompaniesAsync(CancellationToken.None);

        // Neither path may throw an unhandled exception out of this Task.WhenAll — the
        // reconciliation service explicitly catches DbUpdateException and defers to the next pass
        // rather than letting a concurrent-write race escape as an unhandled failure.
        var exception = await Record.ExceptionAsync(() => Task.WhenAll(handlerTask, reconcileTask));
        Assert.Null(exception);

        // A follow-up reconciliation pass (simulating the next scheduled run) must converge
        // regardless of which of the two writers "won" the race.
        await RunReconciliationAsync();

        var positions = await GetUserPositionsAsync(employeeId);
        var active = positions.Where(up => up.IsActive(DateTimeOffset.UtcNow)).ToList();
        Assert.Single(active);
        Assert.Equal(newPositionId, active[0].PositionId);
        Assert.Contains(newRoleId, await GetEffectiveRolesAsync(employeeId));
    }

    [Fact]
    public async Task OneEmployeesReconciliationFailure_DoesNotBlockAnotherEmployeeInTheSameCompany()
    {
        // Isolation is per-employee INSIDE ReconcileEmployeeAsync (see PositionRoleReconciliationService),
        // not at the batch-profile-read level — a reader that throws while resolving the whole
        // company's employee profiles fails that whole company/pass, not just one employee. So this
        // test simulates a failure the real code actually isolates: PositionSync.EnsureExistsAsync
        // (via IPositionProfileReader.GetSummaryAsync) throwing while resolving ONE employee's
        // current position, wrapping the real, DI-resolved IPositionProfileReader so every other
        // position profile (including the healthy employee's) still resolves normally.
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var healthyPositionId = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Healthy Role");
        var healthyRoleId = await SeedPositionWithDefaultRoleAsync(companyId, healthyPositionId, "Healthy Role");
        var failingPositionId = await SeedAdditionalPositionProfileAsync(companyId, referenceData, "Failing Role");

        var (healthyEmployeeId, _) = await SeedEmployeeWithUserAsync(companyId, healthyPositionId);
        var (failingEmployeeId, _) = await SeedEmployeeWithUserAsync(companyId, failingPositionId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<PositionRoleReconciliationService>>();
            var audienceReader = scope.ServiceProvider.GetRequiredService<IEmployeeAudienceReader>();
            var realPositionReader = scope.ServiceProvider.GetRequiredService<IPositionProfileReader>();
            var throwingPositionReader = new ThrowingForOnePositionReader(realPositionReader, failingPositionId);
            var positionSync = new PositionSync(db, throwingPositionReader);
            var clock = scope.ServiceProvider.GetRequiredService<HR.SharedKernel.IClock>();
            var service = new PositionRoleReconciliationService(db, audienceReader, positionSync, clock, logger);

            // Must not throw despite one employee's position resolution being broken.
            await service.ReconcileAllCompaniesAsync(CancellationToken.None);
        }

        var healthyEffectiveRoles = await GetEffectiveRolesAsync(healthyEmployeeId);
        Assert.Contains(healthyRoleId, healthyEffectiveRoles);

        // The failing employee got no assignment created for the unresolvable position — isolated
        // and logged, not silently treated as a success.
        var failingPositions = await GetUserPositionsAsync(failingEmployeeId);
        Assert.DoesNotContain(failingPositions, up => up.PositionId == failingPositionId);
    }

    /// <summary>Wraps the real, DI-resolved <see cref="IPositionProfileReader"/> but throws when
    /// resolving one specific position profile's summary — simulating a transient read failure for
    /// exactly one employee's current position while every other position (including the healthy
    /// employee's) still resolves via the real implementation.</summary>
    private sealed class ThrowingForOnePositionReader(
        IPositionProfileReader inner, Guid throwForPositionProfileId) : IPositionProfileReader
    {
        public Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            inner.ExistsAsync(companyId, positionProfileId, cancellationToken);

        public Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(Guid companyId, Guid? departmentId, string title, CancellationToken cancellationToken) =>
            inner.FindActiveMatchesAsync(companyId, departmentId, title, cancellationToken);

        public Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            inner.GetDepartmentIdAsync(companyId, positionProfileId, cancellationToken);

        public Task<PositionProfileSummary?> GetSummaryAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
        {
            if (positionProfileId == throwForPositionProfileId)
                throw new InvalidOperationException("Simulated transient read failure for this position.");
            return inner.GetSummaryAsync(companyId, positionProfileId, cancellationToken);
        }

        public Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken) =>
            inner.GetSummariesAsync(companyId, positionProfileIds, cancellationToken);

        public Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
            inner.GetIdsByDepartmentAsync(companyId, departmentId, cancellationToken);

        public Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
            inner.GetEmploymentDefaultsAsync(companyId, positionProfileId, cancellationToken);

        public Task<IReadOnlyList<Guid>> GetAllActiveIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
            inner.GetAllActiveIdsAsync(companyId, cancellationToken);

        public Task<IReadOnlyList<Guid>> GetAllIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
            inner.GetAllIdsAsync(companyId, cancellationToken);
    }
}
