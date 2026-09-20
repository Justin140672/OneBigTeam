using System.Text.Json;

using Microsoft.Extensions.Hosting;

namespace HR.Web.Services;

/// <summary>
/// P1 "Login as Customer": the HR.Web-side half of establishing the visible support-session
/// banner. Mirrors SupabaseSessionAccessor's own pattern exactly (see its remarks for the full
/// reasoning on why a real HttpContext/cookie read has to happen during the circuit-establishing
/// HTTP request, not later): a separate, HttpOnly, non-bearer cookie carries only the *display*
/// metadata (company id, admin email, expiry) a support session needs for its banner — never the
/// bearer token itself, which lives solely in the existing obt_supabase_at cookie (see
/// Program.cs's /support-session/redeem endpoint, which sets both cookies together after a
/// successful redemption).
/// </summary>
public sealed class SupportSessionCookieAccessor(IHttpContextAccessor httpContextAccessor, SupportSessionState sessionState)
{
    public const string CookieName = "obt_support_session_meta";

    /// <summary>
    /// Re-reads the live cookie whenever a real HttpContext is available (every SSR pass of every
    /// page load) and (de)activates SupportSessionState to match — same "cookie is authoritative,
    /// fail closed when absent" convention as SupabaseSessionAccessor.AccessToken.
    /// </summary>
    public void Synchronize()
    {
        HttpContext? context;
        try
        {
            context = httpContextAccessor.HttpContext;
        }
        catch (ObjectDisposedException)
        {
            // Same documented footgun as SupabaseSessionAccessor.AccessToken: HttpContext can be a
            // stale/disposed reference once its request has completed. No live HttpContext means
            // nothing to synchronize from this call; the existing per-circuit SupportSessionState
            // is left as-is rather than guessed at.
            return;
        }

        if (context is null)
            return;

        var raw = context.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(raw))
        {
            sessionState.Clear();
            return;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SupportSessionCookiePayload>(raw);
            if (payload is null || payload.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                sessionState.Clear();
                return;
            }

            sessionState.Activate(payload.CompanyId, payload.AdminEmail, payload.ExpiresAt);
        }
        catch (JsonException)
        {
            sessionState.Clear();
        }
    }

    public static void SetCookie(
        HttpContext context, Guid companyId, string adminEmail, DateTimeOffset expiresAt, IHostEnvironment environment)
    {
        var payload = JsonSerializer.Serialize(new SupportSessionCookiePayload(companyId, adminEmail, expiresAt));

        context.Response.Cookies.Append(CookieName, payload, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            Path = "/",
            IsEssential = true,
        });
    }

    public static void ClearCookie(HttpContext context, IHostEnvironment environment)
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

    private sealed record SupportSessionCookiePayload(Guid CompanyId, string AdminEmail, DateTimeOffset ExpiresAt);
}
