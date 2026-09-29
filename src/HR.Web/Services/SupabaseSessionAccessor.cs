using Microsoft.Extensions.Hosting;

namespace HR.Web.Services;

// Scoped (per-Blazor-Server-circuit) holder for the real Supabase access token. The token is
// established by a real HTTP request/response hop (/login-complete, /dev/persona-cookie — see
// Program.cs) which sets a Secure, HttpOnly cookie; it is NEVER carried in a URL query parameter
// (see AuthHandoffStore for why the cross-hop value is an opaque single-use code instead).
//
// Blazor Server's interactive circuit is a long-lived SignalR connection, not a sequence of
// ordinary HTTP requests — IHttpContextAccessor.HttpContext is only populated during the initial
// (pre-render / circuit-establishing) HTTP request, not during later interactive event handling.
// So the cookie is read once, here, during that initial request (forced by an early middleware in
// Program.cs) and cached in CircuitSessionState for the lifetime of the DI scope (== the circuit).
// The post-authentication hard navigation to "/" starts a brand-new circuit whose initial request
// carries the freshly set cookie, so the new circuit captures the real token.
//
// The token is consumed via HrApiHttpClientFactory (also Scoped), which is resolved directly by
// each caller's real DI scope — never through a pooled DelegatingHandler. See CircuitSessionState
// and HrApiHttpClientFactory for why: IHttpClientFactory resolves DelegatingHandler dependencies via
// its own internal, HandlerLifetime-scoped container rather than the calling request/circuit's
// scope (a captive dependency), which was the root cause of the original P1 cross-user token leak.
public sealed class SupabaseSessionAccessor(IHttpContextAccessor httpContextAccessor, CircuitSessionState sessionState)
{
    public const string CookieName = "obt_supabase_at";

    public string? AccessToken
    {
        get
        {
            try
            {
                var context = httpContextAccessor.HttpContext;
                if (context is not null)
                {
                    var cookie = context.Request.Cookies[CookieName];

                    sessionState.SetToken(cookie);
                    return cookie;
                }
            }
            catch
            {
            }

            return sessionState.AccessToken;
        }
    }

    public static void SetSessionCookie(
        HttpContext context,
        string accessToken,
        int expiresInSeconds,
        IHostEnvironment environment,
        CircuitSessionState? sessionState = null)
    {
        context.Response.Cookies.Append(CookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
            Path = "/",
            IsEssential = true,
        });

        sessionState?.SetToken(accessToken);
    }

    public static void ClearSessionCookie(HttpContext context, IHostEnvironment environment, CircuitSessionState? sessionState = null)
    {
        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
        });

        sessionState?.Clear();
    }
}
