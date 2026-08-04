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
    private bool _resolved;

    public string? AccessToken
    {
        get
        {
            if (!_resolved)
            {
                _accessToken = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
                _resolved = true;
            }

            return _accessToken;
        }
    }
}
