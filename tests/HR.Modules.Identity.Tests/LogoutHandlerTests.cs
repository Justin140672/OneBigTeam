using HR.Modules.Identity.Features.Logout;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

public class LogoutHandlerTests
{
    private readonly FakeSupabaseAuthGateway _gateway = new();
    private readonly FakeSessionRevocationStore _revocationStore = new();
    private readonly FakeClock _clock = new(new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc));
    private readonly LogoutHandler _handler;

    public LogoutHandlerTests()
        => _handler = new LogoutHandler(_gateway, _revocationStore, _clock, NullLogger<LogoutHandler>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Returns_Success_Without_Calling_Supabase_When_No_Token(string? token)
    {
        var result = await _handler.HandleAsync(token, supabaseAuthUserId: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.SignedOut);
        Assert.Empty(_gateway.SignOutCalls);
    }

    [Fact]
    public async Task Revokes_The_Supabase_Session_When_Token_Present()
    {
        var result = await _handler.HandleAsync("access-token-value", supabaseAuthUserId: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.SignedOut);
        Assert.Equal("access-token-value", Assert.Single(_gateway.SignOutCalls));
    }

    [Fact]
    public async Task Still_Succeeds_When_Supabase_Sign_Out_Fails()
    {
        _gateway.ShouldThrowOnSignOut = true;

        var result = await _handler.HandleAsync("access-token-value", supabaseAuthUserId: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.SignedOut);
    }

    // --- Ticket 13: server-side revocation is the real security boundary, independent of Supabase ---

    [Fact]
    public async Task Records_Server_Side_Revocation_When_Caller_Identity_Is_Known()
    {
        var userId = Guid.NewGuid();

        await _handler.HandleAsync("access-token-value", userId, CancellationToken.None);

        var recorded = Assert.Single(_revocationStore.RevokeCalls);
        Assert.Equal(userId, recorded.SupabaseAuthUserId);
        Assert.Equal(new DateTimeOffset(_clock.UtcNow, TimeSpan.Zero), recorded.RevokedAt);
    }

    [Fact]
    public async Task Does_Not_Record_Revocation_When_Caller_Identity_Is_Unknown()
    {
        // e.g. the presented bearer failed HR.Api's own JWT validation, so no "sub" claim was
        // available to the endpoint — nothing reliably-identified to revoke.
        await _handler.HandleAsync("access-token-value", supabaseAuthUserId: null, CancellationToken.None);

        Assert.Empty(_revocationStore.RevokeCalls);
    }

    [Fact]
    public async Task Records_Revocation_Even_When_Upstream_Supabase_Sign_Out_Fails()
    {
        // Acceptance criterion: a delayed/failed upstream Supabase sign-out must not weaken the
        // locally-enforced revocation — it is written unconditionally, before the Supabase call.
        _gateway.ShouldThrowOnSignOut = true;
        var userId = Guid.NewGuid();

        var result = await _handler.HandleAsync("access-token-value", userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.SignedOut);
        Assert.Single(_revocationStore.RevokeCalls);
    }

    [Fact]
    public async Task Records_Revocation_Even_When_There_Is_No_Bearer_Token_To_Forward_To_Supabase()
    {
        // The revocation record only needs the validated identity, not a token to hand to Supabase —
        // e.g. a cookie that already expired by the time the browser hit /logout, but whose earlier
        // requests are still what must be invalidated.
        var userId = Guid.NewGuid();

        await _handler.HandleAsync(accessToken: null, userId, CancellationToken.None);

        Assert.Single(_revocationStore.RevokeCalls);
        Assert.Empty(_gateway.SignOutCalls);
    }
}
