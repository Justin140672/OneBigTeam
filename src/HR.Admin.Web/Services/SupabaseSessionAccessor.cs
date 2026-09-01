using Microsoft.Extensions.Hosting;

namespace HR.Admin.Web.Services;

// Mirrors HR.Web.Services.SupabaseSessionAccessor exactly — see that file's remarks for the full
// rationale (Blazor Server's circuit-vs-HttpContext lifetime mismatch, and why a token is NEVER
// carried in a URL: the cross-hop value is an opaque single-use AuthHandoffStore code instead).
// Deliberately duplicated rather than shared: HR.Web and HR.Admin.Web are separate deployable apps
// and this class has no business logic.
public sealed class SupabaseSessionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public const string CookieName = "obt_admin_supabase_at";

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

    public static void SetSessionCookie(HttpContext context, string accessToken, int expiresInSeconds, IHostEnvironment environment)
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
    }

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
