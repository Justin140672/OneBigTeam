using System.IdentityModel.Tokens.Jwt;
using System.Net;

using HR.Api.Authentication;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class EndSupportSessionEndpointTests
{
    private const string Url = "/api/companies/support-session/end";

    private readonly ApiWebApplicationFactory _factory;

    public EndSupportSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient SupportClient(SupportSession session)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, session.IssuedByAdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, session.IssuedByAdminEmail);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, session.CompanyId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.SupportSessionHeader, session.Id.ToString());
        return client;
    }

    private async Task<SupportSession> SeedAsync(bool redeem = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var session = SupportSession.Issue(
            Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "reason", $"hash-{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        if (redeem)
        {
            session.Redeem(DateTimeOffset.UtcNow);
        }

        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task Post_EndSupportSession_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Url, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_EndSupportSession_Revokes_Redeemed_Session()
    {
        var session = await SeedAsync();
        using var client = SupportClient(session);

        var response = await client.PostAsync(Url, null);
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        Assert.NotNull((await db.SupportSessions.SingleAsync(s => s.Id == session.Id)).RevokedAt);
    }

    [Fact]
    public async Task Post_EndSupportSession_Returns_BadRequest_When_Already_Revoked()
    {
        var session = await SeedAsync();
        using var client = SupportClient(session);

        (await client.PostAsync(Url, null)).EnsureSuccessStatusCode();
        var second = await client.PostAsync(Url, null);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Post_EndSupportSession_Returns_NotFound_For_Missing_Session()
    {
        var session = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        db.SupportSessions.Remove(await db.SupportSessions.SingleAsync(s => s.Id == session.Id));
        await db.SaveChangesAsync();
        using var client = SupportClient(session);

        var response = await client.PostAsync(Url, null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Bearer_Validation_Accepts_Token_Before_Revocation_And_Rejects_It_After()
    {
        var session = await SeedAsync();
        var token = IssueToken(session);

        Assert.True(await TokenPassesAsync(token));

        using var client = SupportClient(session);
        (await client.PostAsync(Url, null)).EnsureSuccessStatusCode();

        Assert.False(await TokenPassesAsync(token));
    }

    [Fact]
    public async Task Bearer_Validation_Rejects_Unredeemed_Missing_And_Mismatched_Sessions()
    {
        var unredeemed = await SeedAsync(redeem: false);
        Assert.False(await TokenPassesAsync(IssueToken(unredeemed)));

        var missing = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            db.SupportSessions.Remove(await db.SupportSessions.SingleAsync(s => s.Id == missing.Id));
            await db.SaveChangesAsync();
        }

        Assert.False(await TokenPassesAsync(IssueToken(missing)));

        var valid = await SeedAsync();
        var tokenForOtherCompany = IssueToken(valid, companyId: Guid.NewGuid());
        Assert.False(await TokenPassesAsync(tokenForOtherCompany));
        Assert.True(await TokenPassesAsync(IssueToken(valid)));
    }

    [Fact]
    public async Task Bearer_Validation_Rejects_Token_For_Expired_Session()
    {
        var session = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var tracked = await db.SupportSessions.SingleAsync(s => s.Id == session.Id);
            db.Entry(tracked).Property(s => s.ExpiresAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.False(await TokenPassesAsync(IssueToken(session)));
    }

    [Fact]
    public async Task Bearer_Validation_Fails_Closed_When_State_Cannot_Be_Established()
    {
        var session = await SeedAsync();
        var token = IssueToken(session);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ISupportSessionStateValidator>(new ThrowingValidator())
            .BuildServiceProvider();

        Assert.False(await TokenPassesAsync(token, services));
    }

    private string IssueToken(SupportSession session, Guid? companyId = null) =>
        _factory.Services.GetRequiredService<ISupportSessionTokenIssuer>().IssueToken(
            session.Id,
            companyId ?? session.CompanyId,
            session.IssuedByAdminUserId,
            session.IssuedByAdminEmail,
            DateTimeOffset.UtcNow.AddMinutes(20));

    private async Task<bool> TokenPassesAsync(string token, IServiceProvider? requestServices = null)
    {
        var options = new JwtBearerOptions();
        SupportSessionJwtBearerConfiguration.ConfigureValidation(
            options, _factory.Services.GetRequiredService<IConfiguration>());

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, options.TokenValidationParameters, out _);

        using var scope = _factory.Services.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = requestServices ?? scope.ServiceProvider };
        var context = new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme(SupportSessionJwtBearerConfiguration.SchemeName, null, typeof(JwtBearerHandler)),
            options)
        {
            Principal = principal,
        };

        await options.Events.TokenValidated(context);

        return context.Result?.Failure is null && context.Principal is not null;
    }

    private sealed class ThrowingValidator : ISupportSessionStateValidator
    {
        public Task<bool> IsActiveAsync(
            Guid supportSessionId, Guid companyId, Guid adminUserId, string? adminEmail, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("state store unavailable");
    }
}
