namespace HR.Admin.Web.Services;

// Mirrors HR.Web.Services.SupabaseSessionAccessor exactly — see that file's remarks for the full
// rationale (Blazor Server's circuit-vs-HttpContext lifetime mismatch). Deliberately duplicated
// rather than shared: HR.Web and HR.Admin.Web are separate deployable apps per the deployment
// architecture (specifications/architecture/08-deployment-architecture.md), and this class has no
// business logic — it's a thin, app-local session/cookie accessor.
public sealed class SupabaseSessionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public const string CookieName = "obt_admin_supabase_at";

    private string? _accessToken;
    private bool _initializedFromUrl;

    public string? AccessToken
    {
        get
        {
            if (_initializedFromUrl)
                return _accessToken;

            return httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
        }
    }

    public void Initialize(string accessToken)
    {
        _accessToken = accessToken;
        _initializedFromUrl = true;
    }

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
}
