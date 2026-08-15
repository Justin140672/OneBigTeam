using HR.Admin.Web.Components;
using HR.Admin.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Syncfusion.Blazor;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SupabaseSessionAccessor>();
builder.Services.AddTransient<SupabaseAuthDelegatingHandler>();

builder.Services.AddHttpClient("hrapi", c =>
{
    var apiBaseUrl =
        builder.Configuration["services:api:https:0"] ??
        builder.Configuration["services:api:http:0"] ??
        throw new InvalidOperationException("API base URL is missing. Expected services:api:https:0 or services:api:http:0.");

    c.BaseAddress = new Uri(apiBaseUrl);
})
.AddHttpMessageHandler<SupabaseAuthDelegatingHandler>()
// SocketsHttpHandler's default PooledConnectionLifetime is infinite, so a connection idle long
// enough can be silently closed server-side by Kestrel's own keep-alive timeout while the pool
// still considers it valid — the next request reused from the pool then fails mid-flight with an
// OperationCanceledException while the server is reading the request body. Bounding the lifetime
// well under Kestrel's default 130s keep-alive timeout forces proactive recycling instead.
//
// CertificateRevocationCheckMode = NoCheck: every new connection (including the periodic
// recycling above) re-runs the TLS handshake, which by default performs an online CRL/OCSP
// revocation check against Aspire's local HTTPS dev certificate. That check can't complete
// against a dev cert and stalls for ~15s before SocketsHttpHandler gives up and proceeds anyway
// — this is purely internal service-to-service traffic on localhost, so skipping the check is
// safe and removes that stall entirely.
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromSeconds(60),
    SslOptions = new System.Net.Security.SslClientAuthenticationOptions
    {
        CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
    },
});

builder.Services.AddScoped<CustomerDashboardService>();
builder.Services.AddScoped<CustomerDetailsService>();
builder.Services.AddScoped<CustomerListService>();
builder.Services.AddScoped<CustomerSupportViewService>();
builder.Services.AddScoped<FailedPaymentsService>();
builder.Services.AddScoped<BackgroundJobsService>();
builder.Services.AddScoped<SystemHealthService>();
builder.Services.AddScoped<ApplicationMetricsService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<DeletionQueueService>();
builder.Services.AddScoped<AdminUsersService>();
builder.Services.AddScoped<PlatformSettingsService>();
builder.Services.AddScoped<DevAuthService>();
builder.Services.AddAuthentication("NoOp")
    .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("NoOp", _ => { });
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, AppSessionAuthStateProvider>();
builder.Services.AddCascadingAuthenticationState();

Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
    builder.Configuration["Syncfusion:LicenseKey"] ?? string.Empty);
builder.Services.AddSyncfusionBlazor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.Use(async (context, next) =>
{
    _ = context.RequestServices.GetRequiredService<SupabaseSessionAccessor>().AccessToken;
    await next(context);
});

// Development-only: establishes the Admin Portal's own session cookie for the dev persona
// switcher, mirroring HR.Web's /dev/persona-cookie endpoint exactly (see that file's remarks for
// why this must be a real browser navigation rather than a server-to-server POST).
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/persona-cookie", (HttpContext context, string accessToken, int expiresIn) =>
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return Results.BadRequest();

        SupabaseSessionAccessor.SetSessionCookie(context, accessToken, expiresIn);
        return Results.Redirect($"/?st={Uri.EscapeDataString(accessToken)}&se={expiresIn}");
    }).AllowAnonymous();
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
