using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-03: covers IdentityModule.ReconcilePositionRoleAssignmentsAsync — the additive-only backfill
/// run on every startup so employees created before position/role bridging existed still end up with
/// a matching identity.user_positions row. Built via a minimal ServiceProvider (rather than calling
/// the handler directly) since the method is a `this IServiceProvider` extension that resolves its
/// own scope/services internally.
/// </summary>
[Collection("IdentityDatabase")]
public class ReconcilePositionRoleAssignmentsTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeAudienceReaderForReconcile(
        Guid companyId, IReadOnlyDictionary<Guid, EmployeeAudienceProfile> profilesById) : IEmployeeAudienceReader
    {
        public Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(Guid cId, Guid employeeId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyDictionary<Guid, EmployeeAudienceProfile>> GetEmployeeAudienceProfilesAsync(
            Guid cId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) =>
            Task.FromResult(cId == companyId
                ? profilesById
                : (IReadOnlyDictionary<Guid, EmployeeAudienceProfile>)new Dictionary<Guid, EmployeeAudienceProfile>());

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

    private IServiceProvider BuildServices(IEmployeeAudienceReader audienceReader, IPositionProfileReader positionProfileReader)
    {
        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity")));
        services.AddScoped(_ => audienceReader);
        services.AddScoped(_ => positionProfileReader);
        services.AddScoped(sp => new PositionSync(
            sp.GetRequiredService<IdentityDbContext>(), sp.GetRequiredService<IPositionProfileReader>()));
        return services.BuildServiceProvider();
    }

    private async Task SeedUserProfile(Guid companyId)
    {
        await using var db = fixture.BuildContext();
        db.UserProfiles.Add(UserProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), companyId, $"{Guid.NewGuid():N}@test.com", "Test", "User", Now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ReconcilePositionRoleAssignmentsAsync_Creates_UserPosition_For_Employee_With_No_Active_Assignment()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        await SeedUserProfile(companyId);

        var audienceReader = new FakeAudienceReaderForReconcile(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, positionId) });
        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionId] = new(positionId, "Backfilled Position", null, null, true, null, null),
        };
        var positionReader = new FakePositionProfileReader(summaries: summaries);

        var services = BuildServices(audienceReader, positionReader);
        await services.ReconcilePositionRoleAssignmentsAsync();

        await using var db = fixture.BuildContext();
        var assignment = await db.UserPositions.SingleAsync(up => up.UserId == employeeId);
        Assert.Equal(positionId, assignment.PositionId);
        Assert.True(await db.Positions.AnyAsync(p => p.Id == positionId));
    }

    [Fact]
    public async Task ReconcilePositionRoleAssignmentsAsync_Does_Not_Duplicate_An_Existing_Active_Assignment()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        await SeedUserProfile(companyId);

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(positionId, companyId, "Existing Position", Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, positionId, Now));
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeAudienceReaderForReconcile(
            companyId, new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, positionId) });
        var positionReader = new FakePositionProfileReader();

        var services = BuildServices(audienceReader, positionReader);
        await services.ReconcilePositionRoleAssignmentsAsync();

        await using var db2 = fixture.BuildContext();
        var assignments = await db2.UserPositions.Where(up => up.UserId == employeeId).ToListAsync();
        Assert.Single(assignments); // untouched, not duplicated
    }

    [Fact]
    public async Task ReconcilePositionRoleAssignmentsAsync_Is_Company_Scoped()
    {
        // Same positionProfileId-shaped guid reused across two companies must not cross-contaminate:
        // an assignment created for CompanyA's employee must not satisfy CompanyB's employee holding
        // "the same" position id (a coincidence that should never happen in practice, but the
        // reconciliation must not assume otherwise).
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var sharedPositionId = Guid.NewGuid();
        await SeedUserProfile(companyA);
        await SeedUserProfile(companyB);

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(sharedPositionId, companyA, "Company A Role", Now));
            db.UserPositions.Add(UserPosition.Create(employeeA, sharedPositionId, Now));
            await db.SaveChangesAsync();
        }

        var audienceReader = new CompanyScopedAudienceReader(new Dictionary<Guid, (Guid EmployeeId, Guid? PositionId)[]>
        {
            [companyA] = [(employeeA, sharedPositionId)],
            [companyB] = [(employeeB, sharedPositionId)],
        });
        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [sharedPositionId] = new(sharedPositionId, "Company B Role", null, null, true, null, null),
        };
        var positionReader = new FakePositionProfileReader(summaries: summaries);

        var services = BuildServices(audienceReader, positionReader);
        await services.ReconcilePositionRoleAssignmentsAsync();

        await using var db2 = fixture.BuildContext();
        Assert.Single(await db2.UserPositions.Where(up => up.UserId == employeeA).ToListAsync());
        var employeeBAssignment = await db2.UserPositions.SingleAsync(up => up.UserId == employeeB);
        Assert.Equal(sharedPositionId, employeeBAssignment.PositionId); // created fresh for company B, not skipped
    }

    /// <summary>Multi-company variant of <see cref="FakeAudienceReaderForReconcile"/>, needed for the
    /// isolation test where two different companies must resolve different employee/profile sets.</summary>
    private sealed class CompanyScopedAudienceReader(
        IReadOnlyDictionary<Guid, (Guid EmployeeId, Guid? PositionId)[]> byCompany) : IEmployeeAudienceReader
    {
        public Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(Guid companyId, Guid employeeId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyDictionary<Guid, EmployeeAudienceProfile>> GetEmployeeAudienceProfilesAsync(
            Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
        {
            IReadOnlyDictionary<Guid, EmployeeAudienceProfile> result = byCompany.TryGetValue(companyId, out var entries)
                ? entries.ToDictionary(e => e.EmployeeId, e => new EmployeeAudienceProfile(null, null, e.PositionId))
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
            IReadOnlyList<Guid> result = byCompany.TryGetValue(companyId, out var entries)
                ? entries.Select(e => e.EmployeeId).ToList()
                : [];
            return Task.FromResult(result);
        }
    }
}
