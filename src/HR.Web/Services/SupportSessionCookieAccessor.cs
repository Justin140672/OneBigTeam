using System.Text.Json;

using Microsoft.Extensions.Hosting;

namespace HR.Web.Services;

public sealed class SupportSessionCookieAccessor(IHttpContextAccessor httpContextAccessor, SupportSessionState sessionState)
{
    public const string CookieName = "obt_support_session_meta";

    public void Synchronize()
    {
        HttpContext? context;
        try
        {
            context = httpContextAccessor.HttpContext;
        }
        catch (ObjectDisposedException)
        {
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
