using HR.Admin.Web.Components;
using HR.Admin.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Syncfusion.Blazor;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddHttpContextAccessor();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuthHandoffStore>();
builder.Services.AddScoped<CircuitSessionState>();
builder.Services.AddScoped<SupabaseSessionAccessor>();
builder.Services.AddScoped<HrApiHttpClientFactory>();

builder.Services.AddHttpClient("hrapi", c =>
{
    var apiBaseUrl =
        builder.Configuration["services:api:https:0"] ??
        builder.Configuration["services:api:http:0"] ??
        throw new InvalidOperationException("API base URL is missing. Expected services:api:https:0 or services:api:http:0.");

    c.BaseAddress = new Uri(apiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(130);
})
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
builder.Services.AddScoped<SupportRequestAdminService>();
builder.Services.AddScoped<FailedPaymentsService>();
builder.Services.AddScoped<BackgroundJobsService>();
builder.Services.AddScoped<SystemHealthService>();
builder.Services.AddScoped<ApplicationMetricsService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<OperationalAlertsService>();
builder.Services.AddScoped<DeletionQueueService>();
builder.Services.AddScoped<AdminUsersService>();
builder.Services.AddScoped<PlatformSettingsService>();
builder.Services.AddScoped<SubscriptionPricingService>();
builder.Services.AddScoped<MarketingContentAdminService>();
builder.Services.AddScoped<ProductUpdateService>();
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

app.UseAdminContentSecurityPolicy(app.Environment.IsDevelopment());

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.Use(async (context, next) =>
{
    _ = context.RequestServices.GetRequiredService<SupabaseSessionAccessor>().AccessToken;
    await next(context);
});

// Best-effort server-side Supabase session revocation on sign-out, then clear the cookie and
// return to /login. Mirrors HR.Web's /logout: any failure of the revocation call must NOT block
// sign-out, and nothing token-shaped is ever logged.
app.MapGet("/logout", async (
    HttpContext context,
    IHostEnvironment environment,
    HrApiHttpClientFactory httpClientFactory,
    CircuitSessionState sessionState,
    ILoggerFactory loggerFactory) =>
{
    try
    {
        var http = httpClientFactory.CreateClient();
        using var response = await http.PostAsync("api/logout", content: null, context.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            loggerFactory.CreateLogger("HR.Admin.Web.Logout")
                .LogWarning("Server-side sign-out returned {StatusCode}; clearing the cookie anyway.", (int)response.StatusCode);
        }
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("HR.Admin.Web.Logout")
            .LogWarning(ex, "Server-side sign-out call failed; clearing the cookie anyway.");
    }

    SupabaseSessionAccessor.ClearSessionCookie(context, environment, sessionState);
    return Results.Redirect("/login");
}).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/persona-cookie", (HttpContext context, AuthHandoffStore handoffStore, IHostEnvironment environment, CircuitSessionState sessionState, string? code) =>
    {
        var session = handoffStore.Redeem(code);
        if (session is null)
            return Results.Redirect("/login?error=session");

        SupabaseSessionAccessor.SetSessionCookie(context, session.AccessToken, session.ExpiresInSeconds, environment, sessionState);
        return Results.Redirect("/");
    }).AllowAnonymous();
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
