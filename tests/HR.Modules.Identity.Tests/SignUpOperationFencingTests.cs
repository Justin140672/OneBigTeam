using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class SignUpOperationFencingTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Start = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string> PausePoints =>
        ["company", "seeding", "employee", "mark-admin", "supabase-before", "supabase-after"];

    public static TheoryData<string, bool> PausePointsWithResumeFailure
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var point in new[] { "company", "seeding", "employee", "mark-admin", "supabase-before", "supabase-after" })
            {
                data.Add(point, false);
                data.Add(point, true);
            }

            return data;
        }
    }

    private static SignUpRequest NewRequest() => new(
        CompanyName: "Acme Corp",
        AdminFirstName: "Ada",
        AdminLastName: "Lovelace",
        AdminEmail: $"ada-{Guid.NewGuid():N}@example.com",
        Password: "P@ssw0rd123")
    {
        IdempotencyKey = $"key-{Guid.NewGuid():N}",
    };

    private SignUpHandler Handler(Dependencies deps, DateTime at) =>
        SignUpHandlerFactory.Build(fixture.BuildContext(), deps, new FakeClock(at));

    private SignUpOperationReconciliationJob Job(Dependencies deps, DateTime at)
    {
        var clock = new FakeClock(at);
        var db = fixture.BuildContext();
        var compensator = new SignUpOperationCompensator(
            db, deps.Provisioner, deps.SupabaseAuthGateway, deps.AuditEventPublisher, clock,
            NullLogger<SignUpOperationCompensator>.Instance);
        return new SignUpOperationReconciliationJob(db, compensator, clock, NullLogger<SignUpOperationReconciliationJob>.Instance);
    }

    private static void Install(Dependencies deps, string point, Func<Task> hook)
    {
        switch (point)
        {
            case "company": deps.Provisioner.BeforeProvisionEffect = hook; break;
            case "seeding": deps.DefaultDataSeeder.BeforeEffect = hook; break;
            case "employee": deps.EmployeeProvisioningService.BeforeCreateEffect = hook; break;
            case "mark-admin": deps.EmployeeProvisioningService.BeforeMarkEffect = hook; break;
            case "supabase-before": deps.SupabaseAuthGateway.BeforeCreateEffect = hook; break;
            case "supabase-after": deps.SupabaseAuthGateway.AfterCreateEffect = hook; break;
            default: throw new ArgumentOutOfRangeException(nameof(point));
        }
    }

    private static Func<Task> PauseFirstCaller(PausePoint pause, bool failOnResume)
    {
        var first = true;
        return async () =>
        {
            if (!first)
            {
                return;
            }

            first = false;
            await pause.PauseOnceAsync();
            if (failOnResume)
            {
                throw new InvalidOperationException("resumed worker failed");
            }
        };
    }

    private async Task<SignUpOperation> LoadOperationAsync(Guid operationId)
    {
        await using var db = fixture.BuildContext();
        return await db.SignUpOperations.AsNoTracking().SingleAsync(o => o.Id == operationId);
    }

    private async Task<List<SignUpOperation>> LoadOperationsAsync(string email)
    {
        await using var db = fixture.BuildContext();
        return await db.SignUpOperations.AsNoTracking()
            .Where(o => o.NormalizedEmail == SignUpOperation.Normalize(email))
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
    }

    private async Task AssertSingleCoherentCompletedOutcomeAsync(Dependencies deps, string email, Guid companyId)
    {
        await using var db = fixture.BuildContext();
        var profile = await db.UserProfiles.SingleAsync(p => p.Email == email);
        Assert.Equal(companyId, profile.CompanyId);
        Assert.Equal(deps.SupabaseAuthGateway.UserIdsByEmail[email], profile.SupabaseAuthUserId);
        Assert.DoesNotContain(profile.SupabaseAuthUserId, deps.SupabaseAuthGateway.DeletedUserIds);
        Assert.Equal([companyId], deps.Provisioner.LiveCompanyIds.ToArray());
    }

    [Theory]
    [MemberData(nameof(PausePointsWithResumeFailure))]
    public async Task Original_Worker_Resuming_After_A_Same_Key_Takeover_Cannot_Disturb_The_Completed_Signup(
        string point, bool failOnResume)
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        var pause = new PausePoint();
        Install(deps, point, PauseFirstCaller(pause, failOnResume));

        var original = Task.Run(() => Handler(deps, Start).HandleAsync(request, CancellationToken.None));
        await pause.Entered;

        var takeover = await Handler(deps, Start.AddMinutes(10)).HandleAsync(request, CancellationToken.None);
        pause.Release();
        var resumed = await original;

        Assert.True(takeover.IsSuccess, takeover.IsFailure ? takeover.Error.Message : null);
        Assert.True(resumed.IsSuccess, resumed.IsFailure ? resumed.Error.Message : null);
        Assert.Equal(takeover.Value, resumed.Value);
        Assert.Empty(deps.SupabaseAuthGateway.DeletedUserIds);
        Assert.Empty(deps.Provisioner.DeactivatedCompanyIds);

        var operation = (await LoadOperationsAsync(request.AdminEmail)).Single();
        Assert.Equal(SignUpOperation.StatusCompleted, operation.Status);
        await AssertSingleCoherentCompletedOutcomeAsync(deps, request.AdminEmail, operation.CompanyId);
    }

    [Theory]
    [MemberData(nameof(PausePoints))]
    public async Task Replacement_Signup_Compensates_The_Stale_Operation_And_The_Resuming_Worker_Leaks_Nothing(string point)
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        var pause = new PausePoint();
        Install(deps, point, PauseFirstCaller(pause, failOnResume: false));

        var original = Task.Run(() => Handler(deps, Start).HandleAsync(request, CancellationToken.None));
        await pause.Entered;

        var replacement = request with { IdempotencyKey = $"key-{Guid.NewGuid():N}" };
        var replacementResult = await Handler(deps, Start.AddMinutes(10)).HandleAsync(replacement, CancellationToken.None);
        pause.Release();
        var resumed = await original;

        Assert.True(replacementResult.IsSuccess, replacementResult.IsFailure ? replacementResult.Error.Message : null);
        Assert.True(resumed.IsFailure);

        var operations = await LoadOperationsAsync(request.AdminEmail);
        Assert.Equal(2, operations.Count);
        Assert.Equal(SignUpOperation.StatusFailed, operations[0].Status);
        Assert.Equal(SignUpOperation.StageCompensated, operations[0].Stage);
        Assert.Equal(SignUpOperation.StatusCompleted, operations[1].Status);
        Assert.Equal(replacementResult.Value!.CompanyId, operations[1].CompanyId);
        Assert.Contains(operations[0].CompanyId, deps.Provisioner.DeactivatedCompanyIds);

        await AssertSingleCoherentCompletedOutcomeAsync(deps, request.AdminEmail, operations[1].CompanyId);
        Assert.Equal(
            operations[1].Id.ToString(),
            deps.SupabaseAuthGateway.MetadataByEmail[request.AdminEmail][SignUpOperation.ProvisioningMetadataKey]);
    }

    [Theory]
    [MemberData(nameof(PausePoints))]
    public async Task Resources_Created_By_A_Worker_After_Reconciliation_Compensated_The_Operation_Are_Removed(string point)
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        var pause = new PausePoint();
        Install(deps, point, PauseFirstCaller(pause, failOnResume: false));

        var original = Task.Run(() => Handler(deps, Start).HandleAsync(request, CancellationToken.None));
        await pause.Entered;

        var operationId = (await LoadOperationsAsync(request.AdminEmail)).Single().Id;
        for (var run = 0; run < 3 && (await LoadOperationAsync(operationId)).Status == SignUpOperation.StatusInProgress; run++)
        {
            await Job(deps, Start.AddHours(2)).ExecuteAsync();
        }

        pause.Release();
        var resumed = await original;

        Assert.True(resumed.IsFailure);
        var operation = await LoadOperationAsync(operationId);
        Assert.Equal(SignUpOperation.StatusFailed, operation.Status);
        Assert.Empty(deps.Provisioner.LiveCompanyIds);
        Assert.DoesNotContain(request.AdminEmail, deps.SupabaseAuthGateway.UserIdsByEmail.Keys);

        await using var db = fixture.BuildContext();
        Assert.False(await db.UserProfiles.AnyAsync(p => p.Email == request.AdminEmail));
    }

    [Fact]
    public async Task Reconciliation_Sweep_Removes_Late_Resources_Once_And_Leaves_Foreign_Accounts_Alone()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        deps.DefaultDataSeeder.ShouldThrow = true;
        await Handler(deps, Start).HandleAsync(request, CancellationToken.None);
        deps.DefaultDataSeeder.ShouldThrow = false;
        var operation = (await LoadOperationsAsync(request.AdminEmail)).Single();

        for (var run = 0; run < 3 && (await LoadOperationAsync(operation.Id)).Status == SignUpOperation.StatusInProgress; run++)
        {
            await Job(deps, Start.AddHours(2)).ExecuteAsync();
        }

        var compensated = await LoadOperationAsync(operation.Id);
        Assert.Equal(SignUpOperation.StatusFailed, compensated.Status);
        Assert.Null(compensated.SweptAt);

        var lateUserId = Guid.NewGuid();
        deps.SupabaseAuthGateway.UserIdsByEmail[request.AdminEmail] = lateUserId;
        deps.SupabaseAuthGateway.MetadataByEmail[request.AdminEmail] =
            new Dictionary<string, string> { [SignUpOperation.ProvisioningMetadataKey] = operation.Id.ToString() };
        deps.Provisioner.LiveCompanyIds.Add(operation.CompanyId);

        await Job(deps, Start.AddHours(2).AddMinutes(10)).ExecuteAsync();

        Assert.Contains(lateUserId, deps.SupabaseAuthGateway.DeletedUserIds);
        Assert.DoesNotContain(operation.CompanyId, deps.Provisioner.LiveCompanyIds);
        Assert.NotNull((await LoadOperationAsync(operation.Id)).SweptAt);

        var foreignUserId = Guid.NewGuid();
        deps.SupabaseAuthGateway.UserIdsByEmail[request.AdminEmail] = foreignUserId;
        deps.SupabaseAuthGateway.MetadataByEmail[request.AdminEmail] =
            new Dictionary<string, string> { [SignUpOperation.ProvisioningMetadataKey] = Guid.NewGuid().ToString() };
        await Job(deps, Start.AddHours(2).AddMinutes(20)).ExecuteAsync();

        Assert.DoesNotContain(foreignUserId, deps.SupabaseAuthGateway.DeletedUserIds);
    }

    [Fact]
    public async Task A_Stale_Compensator_Cannot_Deactivate_Or_Delete_The_Resources_Of_A_Completed_Signup()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        deps.DefaultDataSeeder.ShouldThrow = true;
        await Handler(deps, Start).HandleAsync(request, CancellationToken.None);
        deps.DefaultDataSeeder.ShouldThrow = false;

        await using var staleContext = fixture.BuildContext();
        var stale = await staleContext.SignUpOperations.SingleAsync(o => o.NormalizedEmail == SignUpOperation.Normalize(request.AdminEmail));
        stale.TakeLease(new DateTimeOffset(Start), TimeSpan.FromMinutes(3));

        var completed = await Handler(deps, Start.AddMinutes(10)).HandleAsync(request, CancellationToken.None);
        Assert.True(completed.IsSuccess);

        var compensator = new SignUpOperationCompensator(
            staleContext, deps.Provisioner, deps.SupabaseAuthGateway, deps.AuditEventPublisher,
            new FakeClock(Start.AddMinutes(11)), NullLogger<SignUpOperationCompensator>.Instance);
        var result = await compensator.CompensateAsync(stale, "registration_abandoned", "x", releaseKey: true, CancellationToken.None);

        Assert.False(result);
        Assert.Empty(deps.Provisioner.DeactivatedCompanyIds);
        Assert.Empty(deps.SupabaseAuthGateway.DeletedUserIds);
        Assert.Equal(SignUpOperation.StatusCompleted, (await LoadOperationAsync(stale.Id)).Status);
    }

    [Fact]
    public async Task Replacement_Signup_Cannot_Begin_Until_The_Compensation_Of_The_Previous_Operation_Is_Terminal()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        deps.DefaultDataSeeder.ShouldThrow = true;
        await Handler(deps, Start).HandleAsync(request, CancellationToken.None);
        deps.DefaultDataSeeder.ShouldThrow = false;
        deps.Provisioner.ShouldThrowOnDeactivate = true;
        var provisionCalls = deps.Provisioner.CallCount;
        var replacement = request with { IdempotencyKey = $"key-{Guid.NewGuid():N}" };

        var blocked = await Handler(deps, Start.AddMinutes(10)).HandleAsync(replacement, CancellationToken.None);

        Assert.True(blocked.IsFailure);
        var stuck = Assert.Single(await LoadOperationsAsync(request.AdminEmail));
        Assert.Equal(SignUpOperation.StatusInProgress, stuck.Status);
        Assert.Equal(SignUpOperation.StageCompensating, stuck.Stage);
        Assert.Equal(provisionCalls, deps.Provisioner.CallCount);

        deps.Provisioner.ShouldThrowOnDeactivate = false;
        var proceeded = await Handler(deps, Start.AddMinutes(11)).HandleAsync(replacement, CancellationToken.None);

        Assert.True(proceeded.IsSuccess, proceeded.IsFailure ? proceeded.Error.Message : null);
        var operations = await LoadOperationsAsync(request.AdminEmail);
        Assert.Equal(2, operations.Count);
        Assert.Equal(SignUpOperation.StatusFailed, operations[0].Status);
        Assert.Equal(SignUpOperation.StatusCompleted, operations[1].Status);
    }

    [Fact]
    public async Task A_Compensating_Operation_Can_Only_Finish_Compensating_Never_Complete()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        deps.DefaultDataSeeder.ShouldThrow = true;
        await Handler(deps, Start).HandleAsync(request, CancellationToken.None);
        deps.DefaultDataSeeder.ShouldThrow = false;
        deps.Provisioner.ShouldThrowOnDeactivate = true;
        var replacement = request with { IdempotencyKey = $"key-{Guid.NewGuid():N}" };
        await Handler(deps, Start.AddMinutes(10)).HandleAsync(replacement, CancellationToken.None);
        deps.Provisioner.ShouldThrowOnDeactivate = false;
        var provisionCalls = deps.Provisioner.CallCount;

        var retried = await Handler(deps, Start.AddMinutes(11)).HandleAsync(request, CancellationToken.None);

        Assert.True(retried.IsFailure);
        var operation = Assert.Single(await LoadOperationsAsync(request.AdminEmail));
        Assert.Equal(SignUpOperation.StatusFailed, operation.Status);
        Assert.Equal(provisionCalls, deps.Provisioner.CallCount);
        Assert.Contains(operation.CompanyId, deps.Provisioner.DeactivatedCompanyIds);

        await using var db = fixture.BuildContext();
        Assert.False(await db.UserProfiles.AnyAsync(p => p.Email == request.AdminEmail));
    }

    [Fact]
    public async Task Each_Lease_Carries_A_Unique_Fencing_Token_And_Terminal_States_Clear_It()
    {
        var deps = SignUpHandlerFactory.BuildDependencies();
        var request = NewRequest();
        deps.DefaultDataSeeder.ShouldThrow = true;
        await Handler(deps, Start).HandleAsync(request, CancellationToken.None);
        var released = (await LoadOperationsAsync(request.AdminEmail)).Single();
        Assert.Null(released.LeaseToken);

        var pause = new PausePoint();
        deps.DefaultDataSeeder.ShouldThrow = false;
        Install(deps, "employee", PauseFirstCaller(pause, failOnResume: false));
        var running = Task.Run(() => Handler(deps, Start.AddMinutes(10)).HandleAsync(request, CancellationToken.None));
        await pause.Entered;
        var firstToken = (await LoadOperationAsync(released.Id)).LeaseToken;
        Assert.NotNull(firstToken);
        pause.Release();
        await running;

        var completed = await LoadOperationAsync(released.Id);
        Assert.Equal(SignUpOperation.StatusCompleted, completed.Status);
        Assert.Null(completed.LeaseToken);
    }
}
