using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.RevokeSupportSession;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class RevokeSupportSessionIdempotencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
    private const string AdminEmail = "admin@example.com";
    private static readonly Guid ActorId = Guid.NewGuid();

    private sealed class SaveInterceptor(Func<Task> onSaving) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await onSaving();
            return result;
        }
    }

    private sealed class ThrowingPublisher : IAuditEventPublisher
    {
        public bool ThrowOnNext { get; set; }

        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            if (ThrowOnNext)
            {
                ThrowOnNext = false;
                throw new InvalidOperationException("audit store unavailable");
            }

            return Task.CompletedTask;
        }
    }

    private static DbContextOptions<CompaniesDbContext> Options(string databaseName, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<CompaniesDbContext>().UseInMemoryDatabase(databaseName);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }

    private static async Task<Guid> SeedSessionAsync(string databaseName)
    {
        await using var context = new CompaniesDbContext(Options(databaseName));
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), AdminEmail, "reason", $"hash-{Guid.NewGuid():N}", Now);
        session.Redeem(Now.AddMinutes(1));
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private static RevokeSupportSessionHandler BuildHandler(CompaniesDbContext context, IAuditEventPublisher publisher) =>
        new(
            context,
            new FakeCurrentUser(ActorId, email: AdminEmail),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformAdmin:AllowedEmails:0"] = AdminEmail,
            }).Build(),
            new FakeClock(Now.UtcDateTime),
            publisher);

    [Fact]
    public async Task Keyed_Revoke_Commits_Transition_And_Idempotency_Record_Together_And_Replays()
    {
        var db = Guid.NewGuid().ToString("N");
        var sessionId = await SeedSessionAsync(db);
        var publisher = new CapturingAuditEventPublisher();
        var request = new RevokeSupportSessionRequest(sessionId) { IdempotencyKey = "key-1" };

        await using var first = new CompaniesDbContext(Options(db));
        var created = await BuildHandler(first, publisher).HandleAsync(request, CancellationToken.None);

        await using var verify = new CompaniesDbContext(Options(db));
        Assert.NotNull((await verify.SupportSessions.SingleAsync(s => s.Id == sessionId)).RevokedAt);
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync());

        await using var second = new CompaniesDbContext(Options(db));
        var replay = await BuildHandler(second, publisher).HandleAsync(request, CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(created.Value, replay.Value);
        Assert.IsType<SupportSessionRevokedAuditEvent>(Assert.Single(publisher.Published));
    }

    [Fact]
    public async Task Failure_Before_Commit_Persists_Neither_The_Transition_Nor_The_Idempotency_Record()
    {
        var db = Guid.NewGuid().ToString("N");
        var sessionId = await SeedSessionAsync(db);
        var publisher = new CapturingAuditEventPublisher();
        var failing = new SaveInterceptor(() => throw new InvalidOperationException("connection lost"));

        await using var context = new CompaniesDbContext(Options(db, failing));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BuildHandler(context, publisher)
            .HandleAsync(new RevokeSupportSessionRequest(sessionId) { IdempotencyKey = "key-1" }, CancellationToken.None));

        await using var verify = new CompaniesDbContext(Options(db));
        Assert.Null((await verify.SupportSessions.SingleAsync(s => s.Id == sessionId)).RevokedAt);
        Assert.Empty(verify.IdempotencyRecords);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Failure_After_Commit_Then_Same_Key_Replays_The_Original_Success()
    {
        var db = Guid.NewGuid().ToString("N");
        var sessionId = await SeedSessionAsync(db);
        var publisher = new ThrowingPublisher { ThrowOnNext = true };
        var request = new RevokeSupportSessionRequest(sessionId) { IdempotencyKey = "key-1" };

        await using var first = new CompaniesDbContext(Options(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BuildHandler(first, publisher).HandleAsync(request, CancellationToken.None));

        await using var retry = new CompaniesDbContext(Options(db));
        var replay = await BuildHandler(retry, publisher).HandleAsync(request, CancellationToken.None);

        Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Error.Message : null);
        Assert.Equal(sessionId, replay.Value!.SupportSessionId);
        Assert.Equal(Now, replay.Value.RevokedAt);
    }

    [Fact]
    public async Task Key_Reused_For_Another_Session_Is_A_Conflict_And_Does_Not_Revoke_The_Other_Session()
    {
        var db = Guid.NewGuid().ToString("N");
        var first = await SeedSessionAsync(db);
        var other = await SeedSessionAsync(db);
        var publisher = new CapturingAuditEventPublisher();

        await using (var context = new CompaniesDbContext(Options(db)))
        {
            await BuildHandler(context, publisher).HandleAsync(
                new RevokeSupportSessionRequest(first) { IdempotencyKey = "shared-key" }, CancellationToken.None);
        }

        await using var reuse = new CompaniesDbContext(Options(db));
        var result = await BuildHandler(reuse, publisher).HandleAsync(
            new RevokeSupportSessionRequest(other) { IdempotencyKey = "shared-key" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        await using var verify = new CompaniesDbContext(Options(db));
        Assert.Null((await verify.SupportSessions.SingleAsync(s => s.Id == other)).RevokedAt);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task Unkeyed_Revoke_Uses_The_Version_Guarded_Save_And_Publishes_Once()
    {
        var db = Guid.NewGuid().ToString("N");
        var sessionId = await SeedSessionAsync(db);
        var publisher = new CapturingAuditEventPublisher();

        await using var context = new CompaniesDbContext(Options(db));
        var result = await BuildHandler(context, publisher).HandleAsync(new RevokeSupportSessionRequest(sessionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.IdempotencyRecords);
        Assert.IsType<SupportSessionRevokedAuditEvent>(Assert.Single(publisher.Published));
    }
}
