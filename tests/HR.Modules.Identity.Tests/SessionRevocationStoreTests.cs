using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Ticket 13 — persistence behaviour of <see cref="SessionRevocationStore"/> against a real
/// PostgreSQL database (see <see cref="IdentityDatabaseFixture"/>). Complements
/// <c>LogoutHandlerTests</c> (which uses <c>FakeSessionRevocationStore</c> and never touches a real
/// database) by proving the actual EF Core upsert/forward-only semantics that
/// <see cref="SessionRevocation.RevokeAsOf"/> and <see cref="SessionRevocationStore"/> are supposed
/// to provide.
/// </summary>
[Collection("IdentityDatabase")]
public sealed class SessionRevocationStoreTests
{
    private readonly IdentityDatabaseFixture _fixture;

    public SessionRevocationStoreTests(IdentityDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RevokeAsync_Then_GetRevokedAtAsync_Returns_The_Recorded_Instant()
    {
        var userId = Guid.NewGuid();
        var revokedAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        await using (var writeContext = _fixture.BuildContext())
        {
            var store = new SessionRevocationStore(writeContext);
            await store.RevokeAsync(userId, revokedAt, CancellationToken.None);
        }

        await using var readContext = _fixture.BuildContext();
        var readStore = new SessionRevocationStore(readContext);
        var result = await readStore.GetRevokedAtAsync(userId, CancellationToken.None);

        Assert.Equal(revokedAt, result);
    }

    [Fact]
    public async Task GetRevokedAtAsync_Returns_Null_For_A_User_Who_Was_Never_Revoked()
    {
        await using var context = _fixture.BuildContext();
        var store = new SessionRevocationStore(context);

        var result = await store.GetRevokedAtAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task A_Different_Users_Revocation_Does_Not_Affect_This_Users_Result()
    {
        var revokedUser = Guid.NewGuid();
        var untouchedUser = Guid.NewGuid();
        var revokedAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        await using (var writeContext = _fixture.BuildContext())
        {
            var store = new SessionRevocationStore(writeContext);
            await store.RevokeAsync(revokedUser, revokedAt, CancellationToken.None);
        }

        await using var readContext = _fixture.BuildContext();
        var readStore = new SessionRevocationStore(readContext);

        Assert.Null(await readStore.GetRevokedAtAsync(untouchedUser, CancellationToken.None));
        Assert.Equal(revokedAt, await readStore.GetRevokedAtAsync(revokedUser, CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_With_An_Earlier_Second_Timestamp_Does_Not_Move_The_Revocation_Backward()
    {
        var userId = Guid.NewGuid();
        var laterInstant = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var earlierInstant = laterInstant.AddMinutes(-5);

        await using (var context = _fixture.BuildContext())
        {
            var store = new SessionRevocationStore(context);
            await store.RevokeAsync(userId, laterInstant, CancellationToken.None);
        }

        await using (var context = _fixture.BuildContext())
        {
            var store = new SessionRevocationStore(context);
            // Simulates an out-of-order concurrent logout write arriving after a later one already
            // landed — must never un-revoke a session a later logout already covered.
            await store.RevokeAsync(userId, earlierInstant, CancellationToken.None);
        }

        await using var readContext = _fixture.BuildContext();
        var result = await new SessionRevocationStore(readContext)
            .GetRevokedAtAsync(userId, CancellationToken.None);

        Assert.Equal(laterInstant, result);
    }

    [Fact]
    public async Task RevokeAsync_With_A_Later_Second_Timestamp_Does_Move_The_Revocation_Forward()
    {
        var userId = Guid.NewGuid();
        var earlierInstant = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var laterInstant = earlierInstant.AddMinutes(5);

        await using (var context = _fixture.BuildContext())
        {
            var store = new SessionRevocationStore(context);
            await store.RevokeAsync(userId, earlierInstant, CancellationToken.None);
        }

        await using (var context = _fixture.BuildContext())
        {
            var store = new SessionRevocationStore(context);
            await store.RevokeAsync(userId, laterInstant, CancellationToken.None);
        }

        await using var readContext = _fixture.BuildContext();
        var result = await new SessionRevocationStore(readContext)
            .GetRevokedAtAsync(userId, CancellationToken.None);

        Assert.Equal(laterInstant, result);
    }
}
