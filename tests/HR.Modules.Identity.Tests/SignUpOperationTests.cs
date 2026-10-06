using System.Text.Json;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class SignUpOperationTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(Now);

    private static SignUpRequest NewRequest(string? key = null) => new(
        CompanyName: "Acme Corp",
        AdminFirstName: "Ada",
        AdminLastName: "Lovelace",
        AdminEmail: $"ada-{Guid.NewGuid():N}@example.com",
        Password: "P@ssw0rd123")
    {
        IdempotencyKey = key ?? $"key-{Guid.NewGuid():N}",
    };

    private SignUpHandler Handler(Dependencies deps, int waitSeconds = 0) =>
        SignUpHandlerFactory.Build(fixture.BuildContext(), deps, Clock, waitSeconds);

    private async Task<SignUpOperation> LoadOperationAsync(string email)
    {
        await using var db = fixture.BuildContext();
        return await db.SignUpOperations.AsNoTracking()
            .SingleAsync(o => o.NormalizedEmail == SignUpOperation.Normalize(email));
    }

    [Fact]
    public async Task Same_Request_And_Key_Replays_The_Original_Success_Without_Reprovisioning()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();

        var first = await Handler(deps).HandleAsync(request, CancellationToken.None);
        var replay = await Handler(deps).HandleAsync(request, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(1, deps.Provisioner.CallCount);
        Assert.Equal(1, deps.DefaultDataSeeder.CallCount);
        Assert.Equal(1, deps.EmployeeProvisioningService.CallCount);
        Assert.Equal(1, deps.SupabaseAuthGateway.CreateUserCallCount);
        Assert.Single(deps.AuditEventPublisher.PublishedEvents.OfType<RegistrationCreatedAuditEvent>());
    }

    [Fact]
    public async Task Same_Key_With_Different_Payload_Is_A_Conflict_And_Provisions_Nothing_More()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        await Handler(deps).HandleAsync(request, CancellationToken.None);

        var result = await Handler(deps).HandleAsync(request with { CompanyName = "Other Corp" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(1, deps.Provisioner.CallCount);
    }

    [Fact]
    public async Task Concurrent_Identical_Submissions_Provision_Exactly_Once()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => Handler(deps, waitSeconds: 20).HandleAsync(request, CancellationToken.None))));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.IsFailure ? r.Error.Message : null));
        Assert.Single(results.Select(r => r.Value!.UserId).Distinct());
        Assert.Equal(1, deps.Provisioner.CallCount);
        Assert.Equal(1, deps.EmployeeProvisioningService.CallCount);
        Assert.Equal(1, deps.SupabaseAuthGateway.CreateUserCallCount);

        await using var db = fixture.BuildContext();
        Assert.Equal(1, await db.UserProfiles.CountAsync(p => p.Email == request.AdminEmail));
        var profile = await db.UserProfiles.SingleAsync(p => p.Email == request.AdminEmail);
        Assert.Equal(3, await db.UserRoles.CountAsync(r => r.UserId == profile.Id));
        Assert.Equal(1, await db.SignUpOperations.CountAsync(o => o.NormalizedEmail == SignUpOperation.Normalize(request.AdminEmail)));
    }

    [Fact]
    public async Task Concurrent_Submissions_For_The_Same_Email_With_Different_Keys_Provision_Once()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        var requests = Enumerable.Range(0, 3).Select(_ => NewRequest() with { AdminEmail = email }).ToArray();

        var results = await Task.WhenAll(requests
            .Select(r => Task.Run(() => Handler(deps).HandleAsync(r, CancellationToken.None))));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(1, deps.SupabaseAuthGateway.CreateUserCallCount);
        Assert.Equal(1, deps.Provisioner.CallCount);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("seeding")]
    [InlineData("employee")]
    [InlineData("mark-admin")]
    [InlineData("supabase")]
    [InlineData("supabase-after-create")]
    [InlineData("local-commit")]
    public async Task Failure_At_Every_Boundary_Can_Be_Resumed_With_The_Same_Key(string boundary)
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        Guid? preInsertedProfileId = null;
        var failedOnce = false;

        switch (boundary)
        {
            case "company": deps.Provisioner.ThrowOnProvision = new InvalidOperationException("boom"); break;
            case "seeding": deps.DefaultDataSeeder.ShouldThrow = true; break;
            case "employee": deps.EmployeeProvisioningService.ShouldFail = true; break;
            case "mark-admin": deps.EmployeeProvisioningService.ShouldThrowOnMark = true; break;
            case "supabase": deps.SupabaseAuthGateway.ShouldThrowOnCreate = true; break;
            case "supabase-after-create":
                deps.SupabaseAuthGateway.FailAfterCreate = _ =>
                    failedOnce ? null : (failedOnce = true) ? new InvalidOperationException("resend failed") : null;
                break;
            case "local-commit":
                preInsertedProfileId = deps.EmployeeProvisioningService.EmployeeIdToReturn;
                await using (var seed = fixture.BuildContext())
                {
                    seed.UserProfiles.Add(UserProfile.Create(
                        preInsertedProfileId.Value, Guid.NewGuid(), Guid.NewGuid(), $"other-{Guid.NewGuid():N}@example.com", "X", "Y", Now));
                    await seed.SaveChangesAsync();
                }

                break;
        }

        var failed = await Handler(deps).HandleAsync(request, CancellationToken.None);

        Assert.True(failed.IsFailure);
        Assert.Equal("registration_failed", failed.Error.Code);
        Assert.Empty(deps.Provisioner.DeactivatedCompanyIds);
        var interrupted = await LoadOperationAsync(request.AdminEmail);
        Assert.Equal(SignUpOperation.StatusInProgress, interrupted.Status);
        Assert.Null(interrupted.LeaseExpiresAt);

        switch (boundary)
        {
            case "company": deps.Provisioner.ThrowOnProvision = null; break;
            case "seeding": deps.DefaultDataSeeder.ShouldThrow = false; break;
            case "employee": deps.EmployeeProvisioningService.ShouldFail = false; break;
            case "mark-admin": deps.EmployeeProvisioningService.ShouldThrowOnMark = false; break;
            case "supabase": deps.SupabaseAuthGateway.ShouldThrowOnCreate = false; break;
            case "local-commit":
                await using (var cleanup = fixture.BuildContext())
                {
                    await cleanup.UserProfiles.Where(p => p.Id == preInsertedProfileId).ExecuteDeleteAsync();
                }

                break;
        }

        var resumed = await Handler(deps).HandleAsync(request, CancellationToken.None);

        Assert.True(resumed.IsSuccess, resumed.IsFailure ? resumed.Error.Message : null);
        Assert.Empty(deps.Provisioner.DeactivatedCompanyIds);
        Assert.Single(deps.Provisioner.RequestedCompanyIds.Distinct());
        Assert.Equal(resumed.Value!.CompanyId, deps.Provisioner.RequestedCompanyIds.First());
        Assert.Equal(1, deps.SupabaseAuthGateway.CreatedUsers.Count);

        var completed = await LoadOperationAsync(request.AdminEmail);
        Assert.Equal(SignUpOperation.StatusCompleted, completed.Status);

        await using var db = fixture.BuildContext();
        Assert.Equal(1, await db.UserProfiles.CountAsync(p => p.Email == request.AdminEmail));
        var profile = await db.UserProfiles.SingleAsync(p => p.Email == request.AdminEmail);
        Assert.Equal(3, await db.UserRoles.CountAsync(r => r.UserId == profile.Id));
        Assert.All(deps.EmployeeProvisioningService.Requests, r => Assert.Equal($"signup:operation:{completed.Id}", r.SourceReference));

        var replay = await Handler(deps).HandleAsync(request, CancellationToken.None);
        Assert.Equal(resumed.Value, replay.Value);
    }

    [Fact]
    public async Task Resume_After_Supabase_Account_Was_Created_Adopts_It_Instead_Of_Creating_A_Second_One()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        var failedOnce = false;
        deps.SupabaseAuthGateway.FailAfterCreate = _ => failedOnce ? null : (failedOnce = true) ? new InvalidOperationException("resend failed") : null;

        await Handler(deps).HandleAsync(request, CancellationToken.None);
        var createdId = deps.SupabaseAuthGateway.UserIdsByEmail[request.AdminEmail];
        var resumed = await Handler(deps).HandleAsync(request, CancellationToken.None);

        Assert.True(resumed.IsSuccess);
        Assert.Single(deps.SupabaseAuthGateway.CreatedUsers);
        Assert.Single(deps.SupabaseAuthGateway.ResentEmails);
        await using var db = fixture.BuildContext();
        var profile = await db.UserProfiles.SingleAsync(p => p.Email == request.AdminEmail);
        Assert.Equal(createdId, profile.SupabaseAuthUserId);
    }

    [Fact]
    public async Task Exhausted_Attempts_Compensate_Company_And_Supabase_Account_And_Release_The_Key()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        var blockedProfileId = deps.EmployeeProvisioningService.EmployeeIdToReturn;
        await using (var seed = fixture.BuildContext())
        {
            seed.UserProfiles.Add(UserProfile.Create(
                blockedProfileId, Guid.NewGuid(), Guid.NewGuid(), $"other-{Guid.NewGuid():N}@example.com", "X", "Y", Now));
            await seed.SaveChangesAsync();
        }

        Result<SignUpResponse>? last = null;
        for (var attempt = 0; attempt < SignUpHandler.MaxAttempts; attempt++)
        {
            var result = await Handler(deps).HandleAsync(request, CancellationToken.None);
            Assert.True(result.IsFailure);
            last = result;
        }

        var operation = await LoadOperationAsync(request.AdminEmail);
        Assert.Equal(SignUpOperation.StatusFailed, operation.Status);
        Assert.Equal(SignUpOperation.StageCompensated, operation.Stage);
        Assert.Null(operation.IdempotencyKey);
        Assert.Contains(operation.CompanyId, deps.Provisioner.DeactivatedCompanyIds);
        Assert.Single(deps.SupabaseAuthGateway.DeletedUserIds);
        Assert.Empty(deps.SupabaseAuthGateway.UserIdsByEmail);
        Assert.Contains(deps.AuditEventPublisher.PublishedEvents.OfType<RegistrationCreatedAuditEvent>(), e => !e.Succeeded);
        Assert.NotNull(last);

        await using (var cleanup = fixture.BuildContext())
        {
            await cleanup.UserProfiles.Where(p => p.Id == blockedProfileId).ExecuteDeleteAsync();
        }

        var retry = await Handler(deps).HandleAsync(request, CancellationToken.None);
        Assert.True(retry.IsSuccess);
    }

    [Fact]
    public async Task A_New_Attempt_For_The_Same_Email_Compensates_The_Abandoned_Operation_So_It_Cannot_Block()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var first = NewRequest();
        var failedOnce = false;
        deps.SupabaseAuthGateway.FailAfterCreate = _ => failedOnce ? null : (failedOnce = true) ? new InvalidOperationException("resend failed") : null;

        await Handler(deps).HandleAsync(first, CancellationToken.None);
        var orphanedId = deps.SupabaseAuthGateway.UserIdsByEmail[first.AdminEmail];
        var abandoned = await LoadOperationAsync(first.AdminEmail);

        var second = first with { IdempotencyKey = $"key-{Guid.NewGuid():N}" };
        var result = await Handler(deps).HandleAsync(second, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(abandoned.CompanyId, deps.Provisioner.DeactivatedCompanyIds);
        Assert.Contains(orphanedId, deps.SupabaseAuthGateway.DeletedUserIds);
        Assert.NotEqual(abandoned.CompanyId, result.Value!.CompanyId);
    }

    [Fact]
    public async Task Reconciliation_Job_Compensates_Abandoned_Operations_But_Not_Fresh_Ones()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var abandoned = NewRequest();
        deps.DefaultDataSeeder.ShouldThrow = true;
        await Handler(deps).HandleAsync(abandoned, CancellationToken.None);
        deps.DefaultDataSeeder.ShouldThrow = false;
        var operation = await LoadOperationAsync(abandoned.AdminEmail);

        var freshJob = BuildJob(deps, Now.AddMinutes(5));
        await freshJob.ExecuteAsync();
        Assert.Equal(SignUpOperation.StatusInProgress, (await LoadOperationAsync(abandoned.AdminEmail)).Status);

        var staleJob = BuildJob(deps, Now.AddHours(2));
        await staleJob.ExecuteAsync();

        var after = await LoadOperationAsync(abandoned.AdminEmail);
        Assert.Equal(SignUpOperation.StatusFailed, after.Status);
        Assert.Contains(operation.CompanyId, deps.Provisioner.DeactivatedCompanyIds);

        var retry = await Handler(deps).HandleAsync(abandoned, CancellationToken.None);
        Assert.True(retry.IsSuccess);
    }

    [Fact]
    public async Task Foreign_Existing_Supabase_Account_Is_A_Replayable_Conflict_And_Is_Never_Deleted()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        deps.SupabaseAuthGateway.UserIdsByEmail[request.AdminEmail] = Guid.NewGuid();

        var first = await Handler(deps).HandleAsync(request, CancellationToken.None);
        var replay = await Handler(deps).HandleAsync(request, CancellationToken.None);

        Assert.Equal("conflict", first.Error.Code);
        Assert.Equal("conflict", replay.Error.Code);
        Assert.Empty(deps.SupabaseAuthGateway.DeletedUserIds);
        Assert.Equal(1, deps.Provisioner.CallCount);
        Assert.Single(deps.Provisioner.DeactivatedCompanyIds);
    }

    [Fact]
    public async Task Operation_Row_Never_Contains_The_Password()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        deps.DefaultDataSeeder.ShouldThrow = true;
        await Handler(deps).HandleAsync(request, CancellationToken.None);
        deps.DefaultDataSeeder.ShouldThrow = false;
        await Handler(deps).HandleAsync(request, CancellationToken.None);

        var operation = await LoadOperationAsync(request.AdminEmail);

        Assert.DoesNotContain(request.Password, JsonSerializer.Serialize(operation));
    }

    private async Task<string> LoadRawRowJsonAsync(Guid operationId)
    {
        await using var db = fixture.BuildContext();
        return await db.Database
            .SqlQuery<string>($"select row_to_json(t)::text as \"Value\" from identity.signup_operations t where t.id = {operationId}")
            .SingleAsync();
    }

    [Fact]
    public async Task Persisted_Operation_Holds_Neither_The_Password_Nor_An_Offline_Verifiable_Derivative()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        deps.DefaultDataSeeder.ShouldThrow = true;
        await Handler(deps).HandleAsync(request, CancellationToken.None);
        deps.DefaultDataSeeder.ShouldThrow = false;
        await Handler(deps).HandleAsync(request, CancellationToken.None);

        var operation = await LoadOperationAsync(request.AdminEmail);
        var rawRow = await LoadRawRowJsonAsync(operation.Id);

        Assert.DoesNotContain(request.Password, rawRow);

        static string Sha256Hex(string value) =>
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

        var offlineCandidates = new[]
        {
            Sha256Hex(request.Password),
            Sha256Hex(JsonSerializer.Serialize(request with { IdempotencyKey = null })),
            Sha256Hex(JsonSerializer.Serialize(request)),
        };
        Assert.DoesNotContain(operation.RequestFingerprint, offlineCandidates);
        Assert.Equal(SignUpIdempotencyMaterial.Fingerprint(request), operation.RequestFingerprint);
        Assert.Equal(
            operation.RequestFingerprint,
            SignUpIdempotencyMaterial.Fingerprint(request with { Password = "A-completely-different-1!" }));
    }

    [Fact]
    public async Task Unkeyed_Signup_Persists_No_Request_Fingerprint()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest() with { IdempotencyKey = null };

        var result = await Handler(deps).HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var operation = await LoadOperationAsync(request.AdminEmail);
        Assert.Null(operation.IdempotencyKey);
        Assert.Null(operation.RequestFingerprint);
    }

    [Fact]
    public async Task Completed_Signup_Replays_For_The_Same_Key_Regardless_Of_Password()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        var first = await Handler(deps).HandleAsync(request, CancellationToken.None);

        var replay = await Handler(deps).HandleAsync(request with { Password = "Another-Passw0rd!" }, CancellationToken.None);

        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(1, deps.SupabaseAuthGateway.CreateUserCallCount);
    }

    [Fact]
    public async Task Legacy_Fingerprint_Rows_Replay_Only_For_The_Same_Email()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        var first = await Handler(deps).HandleAsync(request, CancellationToken.None);
        await using (var db = fixture.BuildContext())
        {
            await db.SignUpOperations
                .Where(o => o.IdempotencyKey == request.IdempotencyKey)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.RequestFingerprint, SignUpOperation.LegacyFingerprint));
        }

        var replay = await Handler(deps).HandleAsync(request, CancellationToken.None);
        var otherEmail = await Handler(deps).HandleAsync(
            request with { AdminEmail = $"other-{Guid.NewGuid():N}@example.com" }, CancellationToken.None);

        Assert.Equal(first.Value, replay.Value);
        Assert.True(otherEmail.IsFailure);
        Assert.Equal("conflict", otherEmail.Error.Code);
    }

    [Fact]
    public async Task Reconciliation_Job_Purges_Terminal_Operations_Only_After_The_Retention_Window()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var completed = NewRequest();
        await Handler(deps).HandleAsync(completed, CancellationToken.None);

        await BuildJob(deps, Now + SignUpOperation.Retention - TimeSpan.FromMinutes(1)).ExecuteAsync();
        await using (var db = fixture.BuildContext())
        {
            Assert.True(await db.SignUpOperations.AnyAsync(o => o.IdempotencyKey == completed.IdempotencyKey));
        }

        await BuildJob(deps, Now + SignUpOperation.Retention + TimeSpan.FromMinutes(1)).ExecuteAsync();

        await using var after = fixture.BuildContext();
        Assert.False(await after.SignUpOperations.AnyAsync(o => o.IdempotencyKey == completed.IdempotencyKey));
    }

    private SignUpOperationReconciliationJob BuildJob(Dependencies deps, DateTime at)
    {
        var clock = new FakeClock(at);
        var db = fixture.BuildContext();
        var compensator = new SignUpOperationCompensator(
            db, deps.Provisioner, deps.SupabaseAuthGateway, deps.AuditEventPublisher, clock,
            NullLogger<SignUpOperationCompensator>.Instance);
        return new SignUpOperationReconciliationJob(db, compensator, clock, NullLogger<SignUpOperationReconciliationJob>.Instance);
    }
}
