using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace HR.Web.Services;

public sealed class NoOpAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SupabaseAccessTokenClaimType = "obt:supabase_at";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var accessToken = Context.Request.Cookies[SupabaseSessionAccessor.CookieName];
        if (string.IsNullOrEmpty(accessToken))
            return Task.FromResult(AuthenticateResult.NoResult());

        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(accessToken)));

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, tokenHash),
                new Claim(SupabaseAccessTokenClaimType, accessToken),
            ],
            authenticationType: Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/login");
        return Task.CompletedTask;
    }
}
