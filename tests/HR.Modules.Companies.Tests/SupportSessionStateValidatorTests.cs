using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class SupportSessionStateValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Accepts_Redeemed_Unrevoked_Unexpired_Session_With_Matching_Claims()
    {
        var (validator, session) = await ArrangeAsync(s => s.Redeem(Now));

        Assert.True(await Check(validator, session));
    }

    [Fact]
    public async Task Rejects_Unredeemed_Session()
    {
        var (validator, session) = await ArrangeAsync(_ => { });

        Assert.False(await Check(validator, session));
    }

    [Fact]
    public async Task Rejects_Revoked_Session()
    {
        var (validator, session) = await ArrangeAsync(s =>
        {
            s.Redeem(Now);
            s.Revoke(Now);
        });

        Assert.False(await Check(validator, session));
    }

    [Fact]
    public async Task Rejects_Expired_Session()
    {
        var (validator, session) = await ArrangeAsync(s => s.Redeem(Now), Now.AddMinutes(21));

        Assert.False(await Check(validator, session));
    }

    [Fact]
    public async Task Rejects_Missing_Session()
    {
        var (validator, session) = await ArrangeAsync(s => s.Redeem(Now));

        Assert.False(await validator.IsActiveAsync(Guid.NewGuid(), session.CompanyId, session.IssuedByAdminUserId, session.IssuedByAdminEmail, CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_Mismatched_Company_Admin_Or_Email()
    {
        var (validator, session) = await ArrangeAsync(s => s.Redeem(Now));

        Assert.False(await validator.IsActiveAsync(session.Id, Guid.NewGuid(), session.IssuedByAdminUserId, session.IssuedByAdminEmail, CancellationToken.None));
        Assert.False(await validator.IsActiveAsync(session.Id, session.CompanyId, Guid.NewGuid(), session.IssuedByAdminEmail, CancellationToken.None));
        Assert.False(await validator.IsActiveAsync(session.Id, session.CompanyId, session.IssuedByAdminUserId, "other@example.com", CancellationToken.None));
    }

    private static Task<bool> Check(SupportSessionStateValidator validator, SupportSession s) =>
        validator.IsActiveAsync(s.Id, s.CompanyId, s.IssuedByAdminUserId, s.IssuedByAdminEmail, CancellationToken.None);

    private static async Task<(SupportSessionStateValidator, SupportSession)> ArrangeAsync(
        Action<SupportSession> mutate, DateTimeOffset? at = null)
    {
        var context = new CompaniesDbContext(new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var session = SupportSession.Issue(Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", "hash", Now);
        mutate(session);
        context.SupportSessions.Add(session);
        await context.SaveChangesAsync();
        return (new SupportSessionStateValidator(context, new FakeClock((at ?? Now.AddMinutes(1)).UtcDateTime)), session);
    }
}
