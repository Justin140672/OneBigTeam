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
                    // A real HttpContext is present: its cookie state is authoritative for this
                    // request. Always re-read it — never trust an earlier caller's cached value —
                    // and fail closed (null) if this request carries no session cookie.
                    var cookie = context.Request.Cookies[CookieName];
                    sessionState.SetToken(cookie);
                    return cookie;
                }
            }
            catch
            {
                // stale/disposed HttpContext (documented Blazor Server footgun) — fall through to
                // CircuitSessionState below, which is safe because it is a genuine per-circuit DI
                // object, not a value some other, unrelated caller could have left behind.
            }

            // No live HttpContext (Blazor Server interactive circuit event handling). Return only
            // the token captured earlier in *this circuit's own scoped state* — CircuitSessionState
            // is resolved per-circuit by DI, so it can never hold a value left behind by some other,
            // unrelated circuit or request.
            return sessionState.AccessToken;
        }
    }

    /// <summary>
    /// Sets the session cookie and updates this (real, request-scoped) circuit's own
    /// CircuitSessionState to match. See HR.Web.Services.SupabaseSessionAccessor.SetSessionCookie for
    /// the full "auth changed on an open tab" policy: authentication changes are never applied
    /// in-place on a live circuit. The browser always performs a full top-level navigation afterwards,
    /// tearing down the old circuit/DI scope and starting a fresh one whose own early middleware
    /// re-derives CircuitSessionState AND the cascading AuthenticationState together from the same
    /// cookie — so they can never diverge.
    /// </summary>
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
            // Always Secure outside Development; in Development the site may still be plain-http
            // localhost, where a Secure cookie would simply be dropped.
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
            Path = "/",
            IsEssential = true,
        });

        sessionState?.SetToken(accessToken);
    }

    /// <summary>
    /// Deletes the session cookie and clears this request's CircuitSessionState so that no later
    /// call on this scope — even one that races and finds no live HttpContext — can resume sending
    /// the old bearer token.
    /// </summary>
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
