using System.Security.Cryptography;

namespace HR.Admin.Web.Services;

/// <summary>
/// P1 support-conversation stored-XSS fix — secondary mitigation. The Admin Portal renders
/// tenant-authored support content inside the platform-admin origin, so it sends a Content
/// Security Policy that, even if a sanitiser bug ever let markup through, blocks:
/// <list type="bullet">
/// <item>inline event-handler attributes (<c>onerror</c>, <c>onload</c>, ...) and inline
/// <c>&lt;script&gt;</c> blocks — <c>script-src</c> has no <c>'unsafe-inline'</c>; the only inline
/// script, Blazor's import map, is authorised by a per-request nonce;</item>
/// <item><c>javascript:</c> navigations (also covered by the lack of <c>'unsafe-inline'</c>);</item>
/// <item>scripts from any other origin, plugins (<c>object-src 'none'</c>), frames
/// (<c>frame-src 'none'</c>), framing of the portal (<c>frame-ancestors 'none'</c>),
/// <c>&lt;base&gt;</c> hijacking and off-site form posts.</item>
/// </list>
///
/// Deliberate relaxations (documented so they are not tightened blindly):
/// <list type="bullet">
/// <item><c>'unsafe-eval'</c> in <c>script-src</c>: Syncfusion's client script compiles templates
/// with <c>new Function(...)</c>, and the dev sign-in hands off via JS interop <c>eval</c>. This does
/// not re-enable inline handlers or <c>javascript:</c> URLs.</item>
/// <item><c>'unsafe-inline'</c> in <c>style-src</c>: Syncfusion components and Blazor set inline
/// style attributes. Styles cannot execute script.</item>
/// <item>Development only: <c>localhost</c> script/connect sources for dotnet-watch browser refresh
/// and Visual Studio Browser Link.</item>
/// </list>
///
/// CSP is defence in depth only — the primary control is the shared allow-list sanitiser
/// (<c>HR.SharedKernel.Html.SupportHtmlSanitizer</c>) applied before persistence and at render time.
/// </summary>
public static class AdminContentSecurityPolicy
{
    public const string HeaderName = "Content-Security-Policy";

    /// <summary><see cref="HttpContext.Items"/> key holding this request's script nonce.</summary>
    public const string NonceItemKey = "HR.Admin.Web.CspNonce";

    public static string CreateNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    public static string? GetNonce(HttpContext? context) =>
        context?.Items.TryGetValue(NonceItemKey, out var value) == true ? value as string : null;

    public static string Build(string nonce, bool isDevelopment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        var devSources = isDevelopment ? " http://localhost:* https://localhost:*" : string.Empty;
        var devConnect = isDevelopment ? " ws://localhost:* wss://localhost:* http://localhost:* https://localhost:*" : string.Empty;

        return string.Join("; ",
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}' 'unsafe-eval'{devSources}",
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net",
            "font-src 'self' data: https://fonts.gstatic.com",
            "img-src 'self' data:",
            $"connect-src 'self'{devConnect}",
            "object-src 'none'",
            "frame-src 'none'",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'");
    }

    /// <summary>
    /// Adds the policy (with a fresh per-request nonce) to every response. Register after the
    /// exception-handler / status-code-page middleware so re-executed error pages get it too.
    /// </summary>
    public static IApplicationBuilder UseAdminContentSecurityPolicy(this IApplicationBuilder app, bool isDevelopment) =>
        app.Use(async (context, next) =>
        {
            var nonce = CreateNonce();
            context.Items[NonceItemKey] = nonce;
            context.Response.Headers[HeaderName] = Build(nonce, isDevelopment);
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next(context);
        });
}
