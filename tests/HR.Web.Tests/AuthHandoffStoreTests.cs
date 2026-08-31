using HR.Web.Services;

namespace HR.Web.Tests;

/// <summary>
/// Unit coverage for the server-side auth handoff exchange introduced by the security ticket
/// "Remove authentication tokens from browser-visible URLs". The store must be single-use,
/// time-limited, and must never hand back a session for an unknown/blank/tampered code.
/// </summary>
public class AuthHandoffStoreTests
{
    // A minimal settable TimeProvider stand-in. A dedicated FakeTimeProvider package
    // (Microsoft.Extensions.TimeProvider.Testing) is not currently referenced anywhere in the
    // solution; this local double keeps the test dependency-free while giving the same control.
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private readonly TestTimeProvider _time = new();
    private readonly AuthHandoffStore _store;

    public AuthHandoffStoreTests() => _store = new AuthHandoffStore(_time);

    private static AuthHandoffStore.Session SampleSession() =>
        new(AccessToken: "access-token-value", RefreshToken: "refresh-token-value", ExpiresInSeconds: 3600);

    [Fact]
    public void Issue_Then_Redeem_Returns_The_Same_Session_Values()
    {
        var session = SampleSession();

        var code = _store.Issue(session);
        var redeemed = _store.Redeem(code);

        Assert.NotNull(redeemed);
        Assert.Equal(session.AccessToken, redeemed!.AccessToken);
        Assert.Equal(session.RefreshToken, redeemed.RefreshToken);
        Assert.Equal(session.ExpiresInSeconds, redeemed.ExpiresInSeconds);
    }

    [Fact]
    public void Redeem_Is_Single_Use()
    {
        var code = _store.Issue(SampleSession());

        Assert.NotNull(_store.Redeem(code));
        Assert.Null(_store.Redeem(code));
    }

    [Fact]
    public void Redeem_Returns_Null_After_Ttl_Has_Elapsed()
    {
        var code = _store.Issue(SampleSession());

        _time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));

        Assert.Null(_store.Redeem(code));
    }

    [Fact]
    public void Redeem_Just_Before_Ttl_Still_Succeeds()
    {
        var code = _store.Issue(SampleSession());

        _time.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1));

        Assert.NotNull(_store.Redeem(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-code")]
    public void Redeem_Returns_Null_For_Missing_Blank_Or_Unknown_Codes(string? code)
    {
        Assert.Null(_store.Redeem(code));
    }

    [Fact]
    public void Two_Issue_Calls_Return_Different_Codes()
    {
        var first = _store.Issue(SampleSession());
        var second = _store.Issue(SampleSession());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Issued_Code_Is_Url_Safe()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = _store.Issue(SampleSession());

            Assert.DoesNotContain('+', code);
            Assert.DoesNotContain('/', code);
            Assert.DoesNotContain('=', code);
            Assert.False(string.IsNullOrWhiteSpace(code));
        }
    }
}
