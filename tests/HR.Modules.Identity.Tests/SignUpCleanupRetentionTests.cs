using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Migrations;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class SignUpCleanupRetentionTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Start = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private async Task<SignUpOperation> SeedFailedAsync(DateTime failedAt, string? email = null)
    {
        var operation = SignUpOperation.Claim(
            null, "x", email ?? $"orphan-{Guid.NewGuid():N}@example.com", new DateTimeOffset(failedAt), TimeSpan.FromMinutes(3));
        operation.Fail("registration_abandoned", "Registration could not be completed.", releaseKey: true, new DateTimeOffset(failedAt));
        await using var db = fixture.BuildContext();
        db.SignUpOperations.Add(operation);
        await db.SaveChangesAsync();
        return operation;
    }

    private async Task<Dictionary<Guid, SignUpOperation>> LoadAsync(IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        await using var db = fixture.BuildContext();
        return await db.SignUpOperations.AsNoTracking().Where(o => list.Contains(o.Id)).ToDictionaryAsync(o => o.Id);
    }

    private SignUpOperationReconciliationJob Job(Dependencies deps, DateTime at)
    {
        var clock = new FakeClock(at);
        var db = fixture.BuildContext();
        var compensator = new SignUpOperationCompensator(
            db, deps.Provisioner, deps.SupabaseAuthGateway, deps.AuditEventPublisher, clock,
            NullLogger<SignUpOperationCompensator>.Instance);
        return new SignUpOperationReconciliationJob(db, compensator, clock, NullLogger<SignUpOperationReconciliationJob>.Instance);
    }

    private static DateTime PastRetention(DateTime failedAt) => failedAt + SignUpOperation.Retention + TimeSpan.FromDays(1);

    [Fact]
    public async Task Corrective_Migration_Makes_Legacy_Failed_Operations_Eligible_For_A_Real_Sweep()
    {
        var failed = await SeedFailedAsync(Start);
        var failedOther = await SeedFailedAsync(Start);
        await using (var db = fixture.BuildContext())
        {
            await db.SignUpOperations
                .Where(o => o.Id == failed.Id || o.Id == failedOther.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.SweptAt, new DateTimeOffset(Start)));
            await db.Database.ExecuteSqlRawAsync(ReopenLegacyFailedSignUpOperationSweeps.ReopenSweepsSql);
        }

        var loaded = await LoadAsync([failed.Id, failedOther.Id]);
        Assert.All(loaded.Values, o => Assert.Null(o.SweptAt));

        var deps = SignUpHandlerFactory.BuildDependencies();
        deps.Provisioner.LiveCompanyIds.Add(failed.CompanyId);
        await Job(deps, Start.AddHours(2)).ExecuteAsync();

        Assert.Contains(failed.CompanyId, deps.Provisioner.DeactivatedCompanyIds);
        Assert.All((await LoadAsync([failed.Id, failedOther.Id])).Values, o => Assert.NotNull(o.SweptAt));
    }

    [Fact]
    public async Task Expired_Unswept_Failure_Survives_Purge_While_Sweep_Fails_Then_Is_Swept_On_Retry_And_Purged_Later()
    {
        var failed = await SeedFailedAsync(Start, "keep-" + Guid.NewGuid().ToString("N") + "@example.com");
        await using (var db = fixture.BuildContext())
        {
            await db.SignUpOperations.Where(o => o.Id == failed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.LastError, "boom").SetProperty(o => o.FailureMessage, "personal detail"));
        }

        var deps = SignUpHandlerFactory.BuildDependencies();
        deps.Provisioner.ShouldThrowOnDeactivate = true;
        var late = PastRetention(Start);
        await Job(deps, late).ExecuteAsync();

        var stillThere = (await LoadAsync([failed.Id])).Values.Single();
        Assert.Null(stillThere.SweptAt);
        Assert.Null(stillThere.FailureMessage);
        Assert.Null(stillThere.LastError);
        Assert.Equal(failed.AdminEmail, stillThere.AdminEmail);
        Assert.Equal(failed.CompanyId, stillThere.CompanyId);

        deps.Provisioner.ShouldThrowOnDeactivate = false;
        await Job(deps, late.AddMinutes(10)).ExecuteAsync();

        Assert.Empty(await LoadAsync([failed.Id]));
        Assert.Contains(failed.CompanyId, deps.Provisioner.DeactivatedCompanyIds);
    }

    [Fact]
    public async Task Expired_Unswept_Failure_Releases_Its_Retained_Key_So_A_Different_Request_Can_Claim_It()
    {
        var key = $"retained-{Guid.NewGuid():N}";
        var original = SignUpOperation.Claim(
            key, "fp-original", $"orphan-{Guid.NewGuid():N}@example.com", new DateTimeOffset(Start), TimeSpan.FromMinutes(3));
        original.Fail("conflict", "An account with this email already exists.", releaseKey: false, new DateTimeOffset(Start));
        await using (var db = fixture.BuildContext())
        {
            db.SignUpOperations.Add(original);
            await db.SaveChangesAsync();
        }

        var deps = SignUpHandlerFactory.BuildDependencies();
        deps.Provisioner.ShouldThrowOnDeactivate = true;
        var late = PastRetention(Start);
        await Job(deps, late).ExecuteAsync();

        var retained = (await LoadAsync([original.Id])).Values.Single();
        Assert.Null(retained.IdempotencyKey);
        Assert.Null(retained.RequestFingerprint);
        Assert.Null(retained.SweptAt);
        Assert.Equal(original.AdminEmail, retained.AdminEmail);
        Assert.Equal(original.CompanyId, retained.CompanyId);
        Assert.False(retained.MatchesRequest("anything", original.NormalizedEmail));

        var request = new HR.Modules.Identity.Features.SignUp.SignUpRequest(
            CompanyName: "Other Corp",
            AdminFirstName: "Grace",
            AdminLastName: "Hopper",
            AdminEmail: $"grace-{Guid.NewGuid():N}@example.com",
            Password: "P@ssw0rd123")
        {
            IdempotencyKey = key,
        };
        var result = await SignUpHandlerFactory.Build(fixture.BuildContext(), deps, new FakeClock(late), 0)
            .HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull((await LoadAsync([original.Id])).Values.Single());

        deps.Provisioner.ShouldThrowOnDeactivate = false;
        await Job(deps, late.AddMinutes(10)).ExecuteAsync();

        Assert.Empty(await LoadAsync([original.Id]));
        Assert.Contains(original.CompanyId, deps.Provisioner.DeactivatedCompanyIds);
        await using var after = fixture.BuildContext();
        Assert.True(await after.SignUpOperations.AnyAsync(o => o.IdempotencyKey == key && o.Status == SignUpOperation.StatusCompleted));
    }

    [Fact]
    public void Unexpired_Failure_With_Retained_Key_Still_Replays_And_Redacted_Rows_Never_Match()
    {
        var now = new DateTimeOffset(Start);
        var failed = SignUpOperation.Claim("k", "fp", "a@example.com", now, TimeSpan.FromMinutes(3));
        failed.Fail("conflict", "x", releaseKey: false, now);
        Assert.True(failed.MatchesRequest("fp", "A@EXAMPLE.COM"));
        Assert.False(failed.MatchesRequest("other", "A@EXAMPLE.COM"));

        var unkeyed = SignUpOperation.Claim(null, "fp", "a@example.com", now, TimeSpan.FromMinutes(3));
        Assert.False(unkeyed.MatchesRequest("fp", "A@EXAMPLE.COM"));
    }

    [Fact]
    public async Task Swept_Failures_Are_Only_Deleted_After_The_Retention_Window()
    {
        var failed = await SeedFailedAsync(Start);
        var deps = SignUpHandlerFactory.BuildDependencies();

        await Job(deps, Start + SignUpOperation.Retention - TimeSpan.FromHours(1)).ExecuteAsync();
        Assert.NotNull((await LoadAsync([failed.Id])).Values.Single().SweptAt);

        await Job(deps, PastRetention(Start)).ExecuteAsync();
        Assert.Empty(await LoadAsync([failed.Id]));
    }

    [Fact]
    public async Task A_Backlog_Larger_Than_The_Sweep_Batch_Is_Never_Erased_By_The_Purge_Before_It_Is_Swept()
    {
        var oldest = Start.AddDays(-60);
        var total = SignUpOperationReconciliationJob.BatchSize + 7;
        var ids = new List<Guid>();
        for (var i = 0; i < total; i++)
        {
            ids.Add((await SeedFailedAsync(oldest.AddSeconds(i))).Id);
        }

        var deps = SignUpHandlerFactory.BuildDependencies();
        deps.Provisioner.ShouldThrowOnDeactivate = true;
        var at = Start.AddDays(30);
        await Job(deps, at).ExecuteAsync();
        Assert.Equal(total, (await LoadAsync(ids)).Count);

        deps.Provisioner.ShouldThrowOnDeactivate = false;
        await Job(deps, at.AddMinutes(10)).ExecuteAsync();
        var afterFirst = await LoadAsync(ids);
        Assert.Equal(7, afterFirst.Count);
        Assert.All(afterFirst.Values, o => Assert.Null(o.SweptAt));

        await Job(deps, at.AddMinutes(20)).ExecuteAsync();
        Assert.Empty(await LoadAsync(ids));
    }

    [Fact]
    public async Task Cleanup_Is_Idempotent_And_Never_Deletes_An_Account_With_Mismatched_Ownership()
    {
        var failed = await SeedFailedAsync(Start);
        var deps = SignUpHandlerFactory.BuildDependencies();
        var foreignUserId = Guid.NewGuid();
        deps.SupabaseAuthGateway.UserIdsByEmail[failed.AdminEmail] = foreignUserId;
        deps.SupabaseAuthGateway.MetadataByEmail[failed.AdminEmail] =
            new Dictionary<string, string> { [SignUpOperation.ProvisioningMetadataKey] = Guid.NewGuid().ToString() };

        await Job(deps, Start.AddHours(2)).ExecuteAsync();
        await using (var db = fixture.BuildContext())
        {
            await db.SignUpOperations.Where(o => o.Id == failed.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.SweptAt, (DateTimeOffset?)null));
        }

        await Job(deps, Start.AddHours(3)).ExecuteAsync();

        Assert.DoesNotContain(foreignUserId, deps.SupabaseAuthGateway.DeletedUserIds);
        Assert.Contains(foreignUserId, deps.SupabaseAuthGateway.UserIdsByEmail.Values);
    }

    [Fact]
    public async Task Health_Check_Is_Degraded_Only_When_An_Unswept_Failure_Nears_Its_Retention_Deadline()
    {
        var failed = await SeedFailedAsync(Start);
        await using (var db = fixture.BuildContext())
        {
            await db.SignUpOperations.Where(o => o.Id == failed.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.SweptAt, (DateTimeOffset?)null));
        }

        var early = new SignUpCleanupBacklogHealthCheck(fixture.BuildContext(), new FakeClock(Start.AddDays(1)));
        var nearing = new SignUpCleanupBacklogHealthCheck(
            fixture.BuildContext(),
            new FakeClock(Start + SignUpOperation.Retention - SignUpCleanupBacklogHealthCheck.WarningBeforeRetention + TimeSpan.FromHours(1)));

        var earlyResult = await early.CheckHealthAsync(new HealthCheckContext());
        var nearingResult = await nearing.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, nearingResult.Status);
        Assert.NotEqual(HealthStatus.Degraded, earlyResult.Status);
    }
}
