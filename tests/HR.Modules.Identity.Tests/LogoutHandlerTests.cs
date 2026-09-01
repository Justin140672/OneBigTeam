using HR.Modules.Identity.Features.Logout;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

public class LogoutHandlerTests
{
    private readonly FakeSupabaseAuthGateway _gateway = new();
    private readonly LogoutHandler _handler;

    public LogoutHandlerTests()
        => _handler = new LogoutHandler(_gateway, NullLogger<LogoutHandler>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Returns_Success_Without_Calling_Supabase_When_No_Token(string? token)
    {
        var result = await _handler.HandleAsync(token, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.SignedOut);
        Assert.Empty(_gateway.SignOutCalls);
    }

    [Fact]
    public async Task Revokes_The_Supabase_Session_When_Token_Present()
    {
        var result = await _handler.HandleAsync("access-token-value", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.SignedOut);
        Assert.Equal("access-token-value", Assert.Single(_gateway.SignOutCalls));
    }

    [Fact]
    public async Task Still_Succeeds_When_Supabase_Sign_Out_Fails()
    {
        _gateway.ShouldThrowOnSignOut = true;

        var result = await _handler.HandleAsync("access-token-value", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.SignedOut);
    }
}
