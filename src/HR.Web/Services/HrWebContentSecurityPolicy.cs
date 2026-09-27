using System.Net;
using System.Text.RegularExpressions;
using HR.SharedKernel.Http;

namespace HR.Web.Services;

/// <summary>
/// Content Security Policy for the tenant-facing HR.Web application ([P2] CSP ticket). The policy is
/// built per response with a fresh script nonce and sent on every response that passes through the
/// middleware (pages, re-executed error/404 pages, static assets, minimal-API endpoints).
///
/// <para><b>Source inventory</b> (what HR.Web actually loads, and so what the policy allows):</para>
/// <list type="bullet">
/// <item><b>Scripts</b> — all same-origin: <c>_framework/blazor.web.js</c>, Syncfusion's
/// <c>_content/Syncfusion.Blazor.Core/scripts/syncfusion-blazor.min.js</c> (which lazily loads further
/// same-origin <c>_content/Syncfusion.Blazor.*/scripts/*.js</c>), <c>app.js</c> and the
/// <c>ReconnectModal.razor.js</c> module. Inline scripts are exactly: Blazor's import map and the
/// pre-paint theme script in <c>App.razor</c>, plus the three fragment-to-POST hand-off pages
/// (<c>/verify-email</c>, <c>/platform-admin/activate</c>, <c>/reset-password</c>) in Program.cs.
/// Every one of them carries this response's nonce, so <c>script-src</c> has no
/// <c>'unsafe-inline'</c>: injected inline <c>&lt;script&gt;</c>, inline event-handler attributes and
/// <c>javascript:</c> URLs are all blocked.</item>
/// <item><b>No <c>'unsafe-eval'</c></b>. <c>blazor.web.js</c> (10.0) contains no <c>eval</c>/<c>new Function</c>.
/// Syncfusion 34.2.3's only <c>new Function</c> is the EJ2 string-template compiler
/// (<c>sf.base.compile</c>), reached only for JS-side <i>string</i> templates — chart/accumulation-chart
/// tooltip/crosshair <c>Template</c>s, <c>SfToast</c> templates and the JS tooltip's HTML-parse content.
/// HR.Web uses none of these (its charts only set <c>ChartTooltipSettings Enable="true"</c>; there is no
/// SfTooltip/SfToast; Grid, Diagram, DropDowns, Inputs, Popups, Navigations scripts never call it).
/// Our own only <c>eval</c> (a scroll-into-view JS-interop call in EmployeeLeavingTab) was replaced by the
/// named <c>hrScrollIntoView</c> function in app.js. If a future feature adds a Syncfusion string
/// template it will throw an <c>EvalError</c> — use a Blazor <c>RenderFragment</c> template instead
/// rather than re-adding <c>'unsafe-eval'</c>. (HR.Admin.Web keeps <c>'unsafe-eval'</c> for its own,
/// separately documented reasons.)</item>
/// <item><b>Styles</b> — same-origin CSS (app, scoped CSS, Font Awesome, Syncfusion themes), Google Fonts
/// CSS (<c>https://fonts.googleapis.com</c>) and Bootstrap from <c>https://cdn.jsdelivr.net</c>.
/// <c>'unsafe-inline'</c> stays in <c>style-src</c> only: Blazor and Syncfusion set inline
/// <c>style</c> attributes; styles cannot execute script.</item>
/// <item><b>Fonts</b> — same-origin Font Awesome webfonts, <c>https://fonts.gstatic.com</c>, and
/// <c>data:</c> (Syncfusion themes embed their icon font as a <c>data:</c> URI).</item>
/// <item><b>Images</b> — same-origin logos/favicons, <c>data:</c> (Bootstrap's inline SVG form
/// controls, Syncfusion theme images), and the configured <c>ContentSecurityPolicy:ImageOrigins</c>:
/// profile photos, pending photos and company logos are short-lived Supabase Storage signed URLs
/// (<c>https://&lt;project&gt;.supabase.co/storage/v1/object/sign/...</c>), so production must list the
/// Supabase project origin there. No <c>blob:</c> images are used.</item>
/// <item><b>Connections</b> — the Blazor Server SignalR circuit (and its reconnect) to this same host.
/// <c>'self'</c> covers same-origin https and, in CSP3 browsers, ws/wss; the request host's
/// <c>wss://</c> origin is also listed explicitly for browsers that do not map <c>'self'</c> to
/// WebSockets. All API traffic is server-side (HR.Web → HR.Api), never from the browser.</item>
/// <item><b>Plugins / frames</b> — ReviewCv renders the CV in <c>&lt;object data=...&gt;</c> from the
/// same-origin authenticated proxy <c>/companies/{id}/candidates/{id}/cv/{doc}</c> (which fetches the
/// file server-side, so Supabase and dev local-storage signed URLs never reach the browser), hence
/// <c>object-src 'self'</c> rather than <c>'none'</c>. <c>frame-src 'self'</c> is kept as a safety net
/// in case a browser's PDF viewer treats that plugin document as a nested frame. That proxy route alone
/// is sent <c>frame-ancestors 'self'</c> (<see cref="AllowSameOriginFramingMetadata"/>) so our own page
/// can embed it; everything else is <c>frame-ancestors 'none'</c>.</item>
/// <item><b>Downloads / navigations</b> — base64 <c>data:</c> anchor downloads and <c>window.open</c> of
/// document URLs are navigations, which CSP does not govern; Stripe checkout/portal are top-level
/// navigations too. The only forms post to this origin (<c>form-action 'self'</c>).</item>
/// <item><b>Development only</b> — <c>localhost</c> script/connect sources for dotnet-watch browser
/// refresh / hot reload (<c>aspnetcore-browser-refresh.js</c> + its WebSocket) and Visual Studio Browser
/// Link, and <c>localhost</c> images for the dev local-storage signed route served by HR.Api. These are
/// only added when <see cref="HrWebCspSettings.Create"/> is given a Development host environment.</item>
/// </list>
/// </summary>
public static partial class HrWebContentSecurityPolicy
{
    public const string EnforceHeaderName = "Content-Security-Policy";
    public const string ReportOnlyHeaderName = "Content-Security-Policy-Report-Only";

    /// <summary><see cref="HttpContext.Items"/> key holding this response's script nonce.</summary>
    public const string NonceItemKey = "HR.Web.CspNonce";

    private static readonly string[] DevelopmentHttpSources = ["http://localhost:*", "https://localhost:*"];
    private static readonly string[] DevelopmentConnectSources =
        ["ws://localhost:*", "wss://localhost:*", "http://localhost:*", "https://localhost:*"];

    public static string? GetNonce(HttpContext? context) =>
        context?.Items.TryGetValue(NonceItemKey, out var value) == true ? value as string : null;

    /// <summary>
    /// Builds the policy. <paramref name="requestHost"/> is the browser-facing host (used only for the
    /// explicit <c>wss://</c> SignalR source, and ignored unless it is a plain host[:port]).
    /// <paramref name="allowSameOriginFraming"/> is set only for the ReviewCv proxy route.
    /// </summary>
    public static string Build(string nonce, HrWebCspSettings settings, string? requestHost, bool allowSameOriginFraming = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentNullException.ThrowIfNull(settings);

        var dev = settings.IsDevelopment;
        string[] socket = requestHost is not null && ValidHost().IsMatch(requestHost) ? [$"wss://{requestHost.ToLowerInvariant()}"] : [];

        return new ContentSecurityPolicyBuilder()
            .Add("default-src", "'self'")
            .Add("script-src", ["'self'", ContentSecurityPolicyBuilder.NonceSource(nonce), .. dev ? DevelopmentHttpSources : []])
            .Add("style-src", "'self'", "'unsafe-inline'", "https://fonts.googleapis.com", "https://cdn.jsdelivr.net")
            .Add("font-src", "'self'", "data:", "https://fonts.gstatic.com")
            .Add("img-src", ["'self'", "data:", .. settings.ImageOrigins, .. dev ? DevelopmentHttpSources : []])
            .Add("connect-src", ["'self'", .. socket, .. dev ? DevelopmentConnectSources : []])
            .Add("object-src", "'self'")
            .Add("frame-src", "'self'")
            .Add("frame-ancestors", allowSameOriginFraming ? "'self'" : "'none'")
            .Add("base-uri", "'self'")
            .Add("form-action", "'self'")
            .Build();
    }

    /// <summary>
    /// Adds the policy (fresh nonce per response), <c>X-Content-Type-Options: nosniff</c> and
    /// <c>Referrer-Policy</c>. Register after the exception-handler / status-code-page middleware so
    /// re-executed error pages pass through it again, and before everything that can short-circuit.
    /// </summary>
    public static IApplicationBuilder UseHrWebContentSecurityPolicy(this IApplicationBuilder app, HrWebCspSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return app.Use(async (context, next) =>
        {
            var nonce = ContentSecurityPolicyBuilder.CreateNonce();
            context.Items[NonceItemKey] = nonce;

            var allowFraming = context.GetEndpoint()?.Metadata.GetMetadata<AllowSameOriginFramingMetadata>() is not null;
            var headers = context.Response.Headers;
            headers.Remove(EnforceHeaderName);
            headers.Remove(ReportOnlyHeaderName);
            headers[settings.HeaderName] = Build(nonce, settings, context.Request.Host.Value, allowFraming);
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            await next(context);
        });
    }

    /// <summary>Marks an endpoint whose response our own pages embed (ReviewCv's PDF proxy).</summary>
    public static TBuilder AllowSameOriginFraming<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(AllowSameOriginFramingMetadata.Instance);

    [GeneratedRegex(@"^(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*)(?::[0-9]{1,5})?$")]
    private static partial Regex ValidHost();
}

/// <summary>Endpoint metadata: send <c>frame-ancestors 'self'</c> instead of <c>'none'</c>.</summary>
public sealed class AllowSameOriginFramingMetadata
{
    public static readonly AllowSameOriginFramingMetadata Instance = new();

    private AllowSameOriginFramingMetadata()
    {
    }
}

/// <summary>
/// Environment-resolved CSP inputs. The only way to obtain an instance is <see cref="Create"/>, and it
/// sets <see cref="IsDevelopment"/> from <see cref="IHostEnvironment.IsDevelopment"/> alone — no
/// configuration key can switch the development allowances on — and it refuses (throws at startup)
/// any non-https, wildcard or localhost/loopback image origin outside Development.
/// </summary>
public sealed class HrWebCspSettings
{
    public const string SectionName = "ContentSecurityPolicy";

    private HrWebCspSettings(bool isDevelopment, bool reportOnly, IReadOnlyList<string> imageOrigins)
    {
        IsDevelopment = isDevelopment;
        ReportOnly = reportOnly;
        ImageOrigins = imageOrigins;
    }

    public bool IsDevelopment { get; }

    /// <summary><c>ContentSecurityPolicy:ReportOnly</c> (default false = enforce).</summary>
    public bool ReportOnly { get; }

    /// <summary>Normalised <c>scheme://host[:port]</c> origins from <c>ContentSecurityPolicy:ImageOrigins</c>.</summary>
    public IReadOnlyList<string> ImageOrigins { get; }

    public string HeaderName => ReportOnly
        ? HrWebContentSecurityPolicy.ReportOnlyHeaderName
        : HrWebContentSecurityPolicy.EnforceHeaderName;

    public static HrWebCspSettings Create(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        var isDevelopment = environment.IsDevelopment();
        var section = configuration.GetSection(SectionName);
        var reportOnly = section.GetValue("ReportOnly", defaultValue: false);

        var origins = new List<string>();
        foreach (var raw in section.GetSection("ImageOrigins").GetChildren().Select(c => c.Value))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var origin = NormaliseOrigin(raw.Trim(), isDevelopment);
            if (!origins.Contains(origin, StringComparer.Ordinal))
                origins.Add(origin);
        }

        return new HrWebCspSettings(isDevelopment, reportOnly, origins);
    }

    private static string NormaliseOrigin(string value, bool isDevelopment)
    {
        static InvalidOperationException Invalid(string value, string reason) =>
            new($"{SectionName}:ImageOrigins entry '{value}' is invalid: {reason}");

        if (value.Contains('*'))
            throw Invalid(value, "wildcards are not allowed; list the exact origin.");

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw Invalid(value, "expected an absolute https origin such as https://<project>.supabase.co.");

        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw Invalid(value, "must be an origin only (no path, query, fragment or credentials).");

        if (!isDevelopment)
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw Invalid(value, "only https origins are allowed outside Development.");

            if (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)))
                throw Invalid(value, "localhost/loopback origins are development-only.");
        }

        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}
