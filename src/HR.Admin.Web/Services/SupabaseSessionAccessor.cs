using Microsoft.Extensions.Hosting;

namespace HR.Admin.Web.Services;

// Mirrors HR.Web.Services.SupabaseSessionAccessor exactly — see that file's remarks for the full
// rationale (Blazor Server's circuit-vs-HttpContext lifetime mismatch, and why a token is NEVER
// carried in a URL: the cross-hop value is an opaque single-use AuthHandoffStore code instead).
// Deliberately duplicated rather than shared: HR.Web and HR.Admin.Web are separate deployable apps
// and this class has no business logic.
//
// The token is consumed via HrApiHttpClientFactory (Scoped), resolved directly by each caller's real
// DI scope — never through a pooled DelegatingHandler. The previous implementation's
// SupabaseAuthDelegatingHandler made the captive-dependency bug far worse than HR.Web's by latching
// onto the FIRST captured token forever, so once any admin user's token was cached, every subsequent
// request — including a different, possibly former, platform administrator — silently received that
// same token for as long as the pooled handler lived. CircuitSessionState (a genuine per-circuit DI
// object, not a pooled-handler-resolved dependency) closes that gap.
public sealed class SupabaseSessionAccessor(IHttpContextAccessor httpContextAccessor, CircuitSessionState sessionState)
{
    public const string CookieName = "obt_admin_supabase_at";

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
