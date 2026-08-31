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
// Program.cs) and cached for the lifetime of the DI scope (== the circuit). The post-authentication
// hard navigation to "/" starts a brand-new circuit whose initial request carries the freshly set
// cookie, so the new circuit caches the real token.
//
// SupabaseAuthDelegatingHandler (registered on the "hrapi" HttpClient) reads AccessToken from here
// and attaches it as a Bearer token on every outgoing request.
public sealed class SupabaseSessionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public const string CookieName = "obt_supabase_at";

    private string? _accessToken;
    private bool _captured;

    public string? AccessToken
    {
        get
        {
            if (_captured)
                return _accessToken;

            var fromCookie = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
            if (fromCookie is not null)
            {
                _accessToken = fromCookie;
                _captured = true;
            }

            return _accessToken;
        }
    }

    /// <summary>
    /// Sets the Secure, HttpOnly Supabase access-token session cookie on the current response.
    /// Shared by every place that establishes a real Supabase session from a minimal API endpoint
    /// (the /login-complete real sign-in flow and /dev/persona-cookie). Blazor Server's interactive
    /// circuit cannot set cookies mid-response, so both flows must go through a plain HTTP
    /// request/response endpoint like this one.
    /// </summary>
    public static void SetSessionCookie(HttpContext context, string accessToken, int expiresInSeconds, IHostEnvironment environment)
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
    }

    /// <summary>
    /// Deletes the Supabase access-token session cookie. Used by the "/logout" minimal API endpoint
    /// (see Program.cs). The delete options must mirror the attributes the cookie was written with
    /// or some browsers will not clear it.
    /// </summary>
    public static void ClearSessionCookie(HttpContext context, IHostEnvironment environment)
    {
        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
        });
    }
}
