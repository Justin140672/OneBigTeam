using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class SupportSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_Sets_Expected_Fields_And_20_Minute_Expiry()
    {
        var companyId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        var session = SupportSession.Issue(companyId, adminUserId, "admin@example.com", "Investigating a support ticket", "hash123", Now);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(companyId, session.CompanyId);
        Assert.Equal(adminUserId, session.IssuedByAdminUserId);
        Assert.Equal("admin@example.com", session.IssuedByAdminEmail);
        Assert.Equal("Investigating a support ticket", session.Reason);
        Assert.Equal("hash123", session.TokenHash);
        Assert.Equal(Now, session.CreatedAt);
        Assert.Equal(Now.AddMinutes(20), session.ExpiresAt);
        Assert.Null(session.RedeemedAt);
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public void Redeem_Succeeds_Once()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);

        var result = session.Redeem(Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(1), session.RedeemedAt);
    }

    [Fact]
    public void Redeem_Fails_On_Second_Redeem()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Redeem(Now.AddMinutes(1));

        var result = session.Redeem(Now.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Redeem_Fails_When_Revoked()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Revoke(Now.AddMinutes(1));

        var result = session.Redeem(Now.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Redeem_Fails_When_Expired()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);

        var result = session.Redeem(Now.AddMinutes(20));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Redeem_Fails_When_Well_Past_Expiry()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);

        var result = session.Redeem(Now.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Revoke_Succeeds_Once()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);

        var result = session.Revoke(Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(1), session.RevokedAt);
    }

    [Fact]
    public void Revoke_Fails_On_Second_Revoke()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Revoke(Now.AddMinutes(1));

        var result = session.Revoke(Now.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Revoke_Fails_When_Already_Redeemed()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Redeem(Now.AddMinutes(1));

        var result = session.Revoke(Now.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void IsActive_Returns_True_For_Fresh_Unexpired_Session()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);

        Assert.True(session.IsActive(Now.AddMinutes(1)));
    }

    [Fact]
    public void IsActive_Returns_False_Once_Redeemed()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Redeem(Now.AddMinutes(1));

        Assert.False(session.IsActive(Now.AddMinutes(2)));
    }

    [Fact]
    public void IsActive_Returns_False_Once_Revoked()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        session.Revoke(Now.AddMinutes(1));

        Assert.False(session.IsActive(Now.AddMinutes(2)));
    }

    [Fact]
    public void IsActive_Returns_False_Once_Expired()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);

        Assert.False(session.IsActive(Now.AddMinutes(20)));
    }

    [Fact]
    public void IsActive_Returns_True_Just_Before_Expiry()
    {
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);

        Assert.True(session.IsActive(Now.AddMinutes(19).AddSeconds(59)));
    }
}
