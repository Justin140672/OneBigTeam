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
            // Re-read the live cookie whenever a real HttpContext exposes it — this runs during the
            // SSR pass of EVERY page load, so a persona switch / re-login lands here with the NEW
            // cookie and updates CircuitSessionState for this circuit.
            //
            // Guarded: IHttpContextAccessor.HttpContext can hand back a stale/disposed reference
            // once its request has completed (a documented Blazor Server footgun) — touching
            // Request then throws. Fall through to CircuitSessionState below in that case; this is
            // safe because CircuitSessionState is a genuine per-circuit DI object, not a value that
            // could have been left behind by some other, unrelated caller.
            try
            {
                var context = httpContextAccessor.HttpContext;
                if (context is not null)
                {
                    var cookie = context.Request.Cookies[CookieName];

                    // A real HttpContext is present either way: this is an actual HTTP request, so
                    // its cookie state is authoritative. If the cookie is missing, this request has
                    // no session — fail closed rather than resurrecting a token some earlier,
                    // unrelated caller left behind.
                    sessionState.SetToken(cookie);
                    return cookie;
                }
            }
            catch
            {
                // stale/disposed HttpContext — use the CircuitSessionState value below
            }

            // No live HttpContext (Blazor Server interactive circuit event handling, which never
            // gets one). Return the token captured earlier in *this circuit's own scoped state*
            // only — CircuitSessionState is resolved per-circuit by DI, so it can never hold a value
            // left behind by some other, unrelated circuit or request.
            return sessionState.AccessToken;
        }
    }

    /// <summary>
    /// Sets the Secure, HttpOnly Supabase access-token session cookie on the current response, and
    /// updates this (real, request-scoped) circuit's own CircuitSessionState to match. Shared by
    /// every place that establishes a real Supabase session from a minimal API endpoint (the
    /// /login-complete real sign-in flow and /dev/persona-cookie). Blazor Server's interactive
    /// circuit cannot set cookies mid-response, so both flows must go through a plain HTTP
    /// request/response endpoint like this one — which is also why authentication changes never
    /// happen "in place" on a live circuit: the browser always performs a full top-level navigation
    /// (hardNavigate) afterwards, tearing down the old circuit/DI scope and starting a brand-new one
    /// whose own early middleware (see Program.cs) re-derives CircuitSessionState AND the
    /// cascading AuthenticationState from the same fresh cookie together. There is no code path
    /// where one can update without the other, so they can never diverge — this is the platform's
    /// explicit policy for "auth changed on an open tab" (re-authenticate via a fresh circuit,
    /// never mutate an existing one's identity in place).
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
            // Always Secure outside Development. In Development the site may still be plain-http
            // localhost, where a Secure cookie would simply be dropped by the browser.
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            // Lax (not Strict): the post-authentication landing is reached via a top-level GET
            // navigation/redirect that must still carry the cookie. Lax allows that while still
            // blocking the cookie on cross-site sub-resource and POST requests (CSRF surface).
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
            Path = "/",
            IsEssential = true,
        });

        sessionState?.SetToken(accessToken);
    }

    /// <summary>
    /// Deletes the Supabase access-token session cookie and clears this request's CircuitSessionState.
    /// Used by the "/logout" minimal API endpoint (see Program.cs). The delete options must mirror
    /// the attributes the cookie was written with or some browsers will not clear it.
    /// Clearing CircuitSessionState here (not just the cookie) closes the gap where a later call on
    /// the same scope that races and finds no live HttpContext could otherwise still resolve the old
    /// token from CircuitSessionState — logout now fails closed for every subsequent read in this
    /// scope, regardless of HttpContext availability.
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
