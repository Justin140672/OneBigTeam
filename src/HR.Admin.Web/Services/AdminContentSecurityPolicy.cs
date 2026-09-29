using HR.SharedKernel.Http;

namespace HR.Admin.Web.Services;

public static class AdminContentSecurityPolicy
{
    public const string HeaderName = "Content-Security-Policy";

    public const string NonceItemKey = "HR.Admin.Web.CspNonce";

    public static string CreateNonce() => ContentSecurityPolicyBuilder.CreateNonce();

    public static string? GetNonce(HttpContext? context) =>
        context?.Items.TryGetValue(NonceItemKey, out var value) == true ? value as string : null;

    public static string Build(string nonce, bool isDevelopment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        string[] devSources = isDevelopment ? ["http://localhost:*", "https://localhost:*"] : [];
        string[] devConnect = isDevelopment
            ? ["ws://localhost:*", "wss://localhost:*", "http://localhost:*", "https://localhost:*"]
            : [];

        return new ContentSecurityPolicyBuilder()
            .Add("default-src", "'self'")
            .Add("script-src", ["'self'", ContentSecurityPolicyBuilder.NonceSource(nonce), "'unsafe-eval'", .. devSources])
            .Add("style-src", "'self'", "'unsafe-inline'", "https://fonts.googleapis.com", "https://cdn.jsdelivr.net")
            .Add("font-src", "'self'", "data:", "https://fonts.gstatic.com")
            .Add("img-src", "'self'", "data:")
            .Add("connect-src", ["'self'", .. devConnect])
            .Add("object-src", "'none'")
            .Add("frame-src", "'none'")
            .Add("frame-ancestors", "'none'")
            .Add("base-uri", "'self'")
            .Add("form-action", "'self'")
            .Build();
    }

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
