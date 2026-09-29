using System.Net;
using System.Text.RegularExpressions;
using HR.SharedKernel.Http;

namespace HR.Web.Services;

public static partial class HrWebContentSecurityPolicy
{
    public const string EnforceHeaderName = "Content-Security-Policy";
    public const string ReportOnlyHeaderName = "Content-Security-Policy-Report-Only";

    public const string NonceItemKey = "HR.Web.CspNonce";

    private static readonly string[] DevelopmentHttpSources = ["http://localhost:*", "https://localhost:*"];
    private static readonly string[] DevelopmentConnectSources =
        ["ws://localhost:*", "wss://localhost:*", "http://localhost:*", "https://localhost:*"];

    public static string? GetNonce(HttpContext? context) =>
        context?.Items.TryGetValue(NonceItemKey, out var value) == true ? value as string : null;

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

    public static TBuilder AllowSameOriginFraming<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(AllowSameOriginFramingMetadata.Instance);

    [GeneratedRegex(@"^(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*)(?::[0-9]{1,5})?$")]
    private static partial Regex ValidHost();
}

public sealed class AllowSameOriginFramingMetadata
{
    public static readonly AllowSameOriginFramingMetadata Instance = new();

    private AllowSameOriginFramingMetadata()
    {
    }
}

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

    public bool ReportOnly { get; }

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
