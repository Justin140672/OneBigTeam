namespace HR.Web.Services;

// Scoped (per-Blazor-Server-circuit) holder for the real Supabase access token established by the
// /verify-email minimal API endpoint (see Program.cs). Blazor Server's interactive circuit is a
// long-lived SignalR connection, not a sequence of ordinary HTTP requests — IHttpContextAccessor's
// HttpContext is only populated during the initial (pre-render / circuit-establishing) HTTP
// request, not during subsequent interactive event handling. So the access token is read once,
// here, from the auth cookie during that initial request and cached for the lifetime of the DI
// scope (== the circuit), rather than re-read from IHttpContextAccessor on every hrapi call.
//
// SupabaseAuthDelegatingHandler (registered on the "hrapi" HttpClient) reads AccessToken from this
// accessor and attaches it as a Bearer token on every outgoing request, so HR.Api's real (non-Dev)
// JWT Bearer authentication — and SupabaseCurrentUserResolutionMiddleware downstream of it — can
// resolve the real authenticated user. In Development, HR.Api's DevAuthHandler ignores the
// Authorization header entirely, so this has no effect on the existing dev-persona flow.
public sealed class SupabaseSessionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public const string CookieName = "obt_supabase_at";

    private string? _accessToken;
    private bool _initializedFromUrl;

    public string? AccessToken
    {
        get
        {
            // Confirmed via live diagnosis: Blazor Server's persistent circuit can survive a full
            // browser navigation, keeping this SAME scoped instance alive from before any session
            // cookie existed — IHttpContextAccessor.HttpContext is never reliably available again on
            // that circuit afterward to re-read it. Routes.razor's Initialize(...) — driven by
            // NavigationManager.Uri, which IS reliably available inside the circuit — is the source
            // of truth once a real session has been established; this HttpContext-based read is only
            // a fallback for plain-HTTP-request code paths that run before any circuit exists.
            if (_initializedFromUrl)
                return _accessToken;

            return httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
        }
    }

    /// <summary>
    /// Sets the token from the "st" query-string parameter Routes.razor reads on arrival at "/"
    /// (see /dev/persona-cookie and /login-complete in Program.cs) — the one value proven reliable
    /// across this circuit's whole lifetime, unlike a cookie re-read via HttpContext.
    /// </summary>
    public void Initialize(string accessToken)
    {
        _accessToken = accessToken;
        _initializedFromUrl = true;
    }

    /// <summary>
    /// Sets the HttpOnly Supabase access-token session cookie on the current response. Shared by
    /// every place that establishes a real Supabase session from a minimal API endpoint (the
    /// /login-complete real sign-in flow, and /dev/persona-cookie used by the dev persona switcher)
    /// — Blazor Server's interactive circuit cannot set cookies mid-response, so both flows must go
    /// through a plain HTTP request/response endpoint like this one. Deliberately NOT used by
    /// /verify-email-complete — see that endpoint's remarks in Program.cs.
    /// </summary>
    public static void SetSessionCookie(HttpContext context, string accessToken, int expiresInSeconds)
    {
        context.Response.Cookies.Append(CookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
            Path = "/",
        });
    }

    /// <summary>
    /// Deletes the Supabase access-token session cookie set by <see cref="SetSessionCookie"/>. Used
    /// by the "/logout" minimal API endpoint (see Program.cs) — same "must be a real HTTP
    /// request/response, not mid-circuit" constraint as SetSessionCookie itself.
    /// </summary>
    public static void ClearSessionCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            Path = "/",
        });
    }
}
