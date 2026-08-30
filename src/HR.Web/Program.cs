using HR.Web.Components;
using HR.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Syncfusion.Blazor;

// See HR.Api/Program.cs's identical call for the full rationale — raises the .NET ThreadPool's
// minimum thread counts above their low, core-count-based defaults so a sudden burst of concurrent
// work (many parallel E2E Playwright sessions, each driving its own Blazor Server circuit and firing
// async HTTP calls to hrapi) doesn't queue up faster than the pool's default ramp-up rate. Only
// raises the floor the pool starts warm at; never a ceiling.
ThreadPool.SetMinThreads(Environment.ProcessorCount * 12, Environment.ProcessorCount * 12);

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SupabaseSessionAccessor>();
builder.Services.AddScoped<SupportSessionState>();
builder.Services.AddTransient<SupabaseAuthDelegatingHandler>();

builder.Services.AddHttpClient("hrapi", c =>
{
    var apiBaseUrl =
        builder.Configuration["services:api:https:0"] ??
        builder.Configuration["services:api:http:0"] ??
        throw new InvalidOperationException("API base URL is missing. Expected services:api:https:0 or services:api:http:0.");

    c.BaseAddress = new Uri(apiBaseUrl);
})
// Attaches a real Supabase access token (once one has been established via /verify-email) as a
// Bearer token on every outgoing hrapi request — see SupabaseAuthDelegatingHandler/
// SupabaseSessionAccessor remarks. No-op for the existing Development dev-persona flow.
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

builder.Services.AddScoped<CompanyService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<LocationTypeService>();
builder.Services.AddScoped<LocationService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<InviteService>();
builder.Services.AddScoped<PositionProfileService>();
builder.Services.AddScoped<OnboardingTemplateService>();
builder.Services.AddScoped<PublicHolidayService>();
builder.Services.AddScoped<LeaveService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<ProfilePhotoService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<AssetCategoryService>();
builder.Services.AddScoped<EmploymentTypeService>();
builder.Services.AddScoped<LeaveTypeService>();
builder.Services.AddScoped<LeavePolicyService>();
builder.Services.AddScoped<DocumentTypeService>();
builder.Services.AddScoped<SicknessCategoryService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ProbationService>();
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddScoped<OffboardingService>();
builder.Services.AddScoped<CompensationService>();
builder.Services.AddScoped<EmployeeNoteService>();
builder.Services.AddScoped<AuditHistoryService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<EmployeeTimelineService>();
builder.Services.AddScoped<OrganisationHierarchyBuilder>();
builder.Services.AddScoped<OrganisationChartService>();
builder.Services.AddScoped<SicknessService>();
builder.Services.AddScoped<DevAuthService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<VacancyService>();
builder.Services.AddScoped<CandidateService>();
builder.Services.AddScoped<ApplicationService>();
builder.Services.AddScoped<InterviewService>();
builder.Services.AddScoped<RecruitmentKanbanService>();
builder.Services.AddScoped<ExternalRecruiterService>();
builder.Services.AddScoped<RecruitmentStageService>();
builder.Services.AddScoped<UserAdministrationService>();
builder.Services.AddScoped<DataImportService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<AdministrativeAlertsService>();
builder.Services.AddScoped<CompanyOnboardingService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<SupportService>();
builder.Services.AddScoped<AppSession>();
builder.Services.AddScoped<AuthenticationStateProvider, AppSessionAuthStateProvider>();
builder.Services.AddAuthentication("NoOp")
    .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("NoOp", _ => { });
// Requires an authenticated user on every page by default via Components/Pages/_Imports.razor's
// @attribute [Authorize] (Login.razor overrides it locally with its own [AllowAnonymous]).
// Deliberately NOT a global AddAuthorizationCore FallbackPolicy: a FallbackPolicy applies to every
// endpoint in the app indiscriminately, not just Razor Component pages — in practice that redirected
// static assets (app.js, syncfusion-blazor.min.js) and the Blazor Server SignalR hub (/_blazor) to
// /login too, breaking script loading and the circuit itself. Scoping via the per-page [Authorize]
// attribute keeps enforcement to actual routable pages, which is all RazorComponentsEndpointHandler
// needs to challenge-and-redirect unauthenticated visitors (see NoOpAuthenticationHandler).
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
    builder.Configuration["Syncfusion:LicenseKey"] ?? string.Empty);
builder.Services.AddSyncfusionBlazor();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Required: any endpoint carrying authorization metadata — [Authorize] (Components/Pages/
// _Imports.razor) or even just [AllowAnonymous] (Login.razor, NotFound.razor) — needs
// AuthorizationMiddleware present to interpret that metadata, or ASP.NET Core throws
// InvalidOperationException ("contains authorization metadata, but a middleware was not found...")
// on the first request that matches such an endpoint. Earlier this was deliberately left out
// because a global AddAuthorizationCore FallbackPolicy made EVERY endpoint carry that metadata,
// including static assets and the SignalR hub — breaking script loading and the circuit. Now that
// authorization is scoped to the per-page [Authorize] attribute instead of a blanket FallbackPolicy,
// static assets/the hub carry no metadata at all, so UseAuthorization() has nothing to enforce on
// them and is safe to have back.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Blazor's form-handling middleware throws when a POST arrives without __blazor_form_name.
// This can happen when a previous circuit fails mid-request and the browser retries with a
// stale enhanced-navigation POST. Catch it here and redirect to a clean page rather than
// crashing the request pipeline (which would make the error-UI bleed into the next circuit).
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (InvalidOperationException ex) when (
        ex.Message.StartsWith("The POST request does not specify which form"))
    {
        context.Response.Redirect("/");
    }
});

// Forces SupabaseSessionAccessor to read the obt_supabase_at cookie right now, while HttpContext
// is guaranteed available (this middleware runs for every real HTTP request). Without this,
// SupabaseSessionAccessor's lazy first-access read could happen only once Blazor Server's
// interactive circuit has taken over (a live SignalR connection, not an HTTP request), at which
// point HttpContext is null and the token would be cached as missing for the whole circuit.
app.Use(async (context, next) =>
{
    _ = context.RequestServices.GetRequiredService<SupabaseSessionAccessor>().AccessToken;
    await next(context);
});

// Handles Supabase's verification redirect. Confirmed via live testing against a real Supabase
// project: Supabase uses the IMPLICIT/fragment flow here, not PKCE — it redirects the browser to
// "{redirect_to}#access_token=...&refresh_token=...&expires_in=...&type=...", not
// "{redirect_to}?code=...". A URL fragment is never sent to the server (browser-only), so this
// first hop can only be a client-side hand-off: a tiny static page whose inline script reads
// window.location.hash and immediately navigates to /verify-email-complete with the same values
// as a normal query string, which *does* reach the server. Keep this endpoint doing nothing else —
// no HttpContext/cookie work happens here, since a fragment-only request has nothing to act on
// until the follow-up request arrives.
app.MapGet("/verify-email", () => Results.Content("""
    <!DOCTYPE html>
    <html>
    <head><title>Confirming your account…</title></head>
    <body>
    <script>
        var params = new URLSearchParams(window.location.hash.slice(1));
        var accessToken = params.get('access_token');
        if (accessToken) {
            var query = new URLSearchParams({
                access_token: accessToken,
                refresh_token: params.get('refresh_token') || '',
                expires_in: params.get('expires_in') || ''
            });
            window.location.replace('/verify-email-complete?' + query.toString());
        } else {
            window.location.replace('/verify-email-error');
        }
    </script>
    </body>
    </html>
    """, "text/html")).AllowAnonymous();

// The real second half of the flow — reached via a normal navigation (query string, not fragment),
// so this can act as a plain request/response minimal API endpoint exactly like the rest of this
// file's reasoning around Blazor Server's response-already-started constraints. Takes the access
// token HR.Web's browser-side script already extracted, calls HR.Api's now-authenticated
// /api/verify-email with it as a Bearer token (HR.Api's existing JWT Bearer validation verifies
// the token itself — this endpoint does not need to, and never did trust it blindly) purely to
// activate the company, then sends the admin to /login to sign in with their own credentials.
//
// Deliberately does NOT establish a session here (no SetSessionCookie, no "st" carry-over) even
// though the access token this endpoint holds could sign them straight in — confirmed via live
// testing that doing so is unsafe: if the browser already has an existing session cookie from a
// different signed-in user (e.g. a dev persona) that a Blazor Server circuit is still holding onto,
// this hop can land the visitor in THAT other account instead of the one they just verified.
// Requiring an explicit login here sidesteps the whole stale-session/circuit-reuse class of bug.
app.MapGet("/verify-email-complete", async (
    string? access_token,
    IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(access_token))
    {
        return Results.Redirect("/verify-email-error");
    }

    var http = httpClientFactory.CreateClient("hrapi");
    http.DefaultRequestHeaders.Authorization = new("Bearer", access_token);

    HttpResponseMessage response;
    try
    {
        response = await http.PostAsync("api/verify-email", content: null);
    }
    catch (HttpRequestException)
    {
        return Results.Redirect("/verify-email-error");
    }

    if (!response.IsSuccessStatusCode)
    {
        return Results.Redirect("/verify-email-error");
    }

    return Results.Redirect("/login?verified=true");
}).AllowAnonymous();

// Handles Supabase's password-recovery redirect — same implicit/fragment flow as /verify-email
// above (Supabase uses the identical redirect mechanism for both — see that endpoint's remarks).
// Unlike /verify-email-complete, the next hop here (/reset-password-complete) needs to render an
// actual form (the new password), not just set a cookie and redirect — so it's a normal Blazor
// page reached via plain navigation, rather than another raw minimal-API hop. A missing token is
// still forwarded there (as an empty query value) rather than redirected elsewhere, so that page
// can show its own "this link is invalid or has expired" message with reset-password-specific
// copy and a link back to /forgot-password, instead of reusing /verify-email-error's mismatched
// wording.
app.MapGet("/reset-password", () => Results.Content("""
    <!DOCTYPE html>
    <html>
    <head><title>Confirming your request…</title></head>
    <body>
    <script>
        var params = new URLSearchParams(window.location.hash.slice(1));
        var accessToken = params.get('access_token') || '';
        window.location.replace('/reset-password-complete?access_token=' + encodeURIComponent(accessToken));
    </script>
    </body>
    </html>
    """, "text/html")).AllowAnonymous();

// The real (non-dev-gated) counterpart to /dev/persona-cookie below, for Login.razor's genuine
// Supabase sign-in (HR.Modules.Identity's Login feature, POST /api/login). Same constraint as
// /verify-email-complete/dev/persona-cookie: Blazor Server's interactive circuit can't set cookies
// mid-render, so this must be a real browser navigation reached via hardNavigate — Login.razor
// already has the tokens by the time it calls this, having gotten them from api/login itself.
app.MapGet("/login-complete", (HttpContext context, string accessToken, int expiresIn) =>
{
    if (string.IsNullOrWhiteSpace(accessToken))
        return Results.BadRequest();

    SupabaseSessionAccessor.SetSessionCookie(context, accessToken, expiresIn);

    // See /dev/persona-cookie's remarks (and Routes.razor) for why the token is also carried one
    // hop further via the URL, not just the cookie.
    return Results.Redirect($"/?st={Uri.EscapeDataString(accessToken)}&se={expiresIn}");
}).AllowAnonymous();

// Clears the Supabase session cookie and returns to /login. Same "must be a real HTTP
// request/response, not mid-circuit" constraint as /login-complete above — MainLayout.razor's
// logout button (top bar, and the blocking first-login "Complete your employee profile" dialog)
// must navigate here via app.js's hardNavigate, not NavigationManager.NavigateTo.
app.MapGet("/logout", (HttpContext context) =>
{
    SupabaseSessionAccessor.ClearSessionCookie(context);
    return Results.Redirect("/login");
}).AllowAnonymous();

// Development-only: establishes a real Supabase session cookie for the dev persona switcher.
// Blazor Server's interactive circuit (a live SignalR connection, not a normal HTTP
// request/response) cannot set cookies mid-render — same constraint as /verify-email-complete
// above. This must be a real browser navigation (GET + query string, not a server-to-server POST):
// DevAuthService.SwitchAsync calls HR.Api's /api/dev/persona/{userId} (which performs a real
// Supabase password-grant login) to get tokens, then MainLayout.razor navigates the actual browser
// here via NavigationManager.NavigateTo(..., forceLoad: true) so the Set-Cookie header lands in the
// user's real cookie jar — a POST issued from an HttpClient inside the Blazor Server process would
// only set a cookie on that throwaway HttpClient, never on the user's browser.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/persona-cookie", (HttpContext context, string accessToken, int expiresIn) =>
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return Results.BadRequest();

        SupabaseSessionAccessor.SetSessionCookie(context, accessToken, expiresIn);

        // Carries the token one hop further via the URL, not just the cookie — confirmed (via
        // extensive live diagnosis) that Blazor Server's persistent circuit can survive a full
        // browser navigation, keeping its own SupabaseSessionAccessor instance alive from BEFORE the
        // cookie existed; IHttpContextAccessor.HttpContext is never available again on that circuit
        // to re-read it. NavigationManager.Uri, unlike HttpContext, IS reliably available inside an
        // interactive circuit — Routes.razor reads this query string on arrival and scrubs it
        // immediately after (see its remarks).
        return Results.Redirect($"/?st={Uri.EscapeDataString(accessToken)}&se={expiresIn}");
    }).AllowAnonymous();
}

// "Login As Customer" support-session redemption (Support epic). A platform administrator
// generates a support session from the Admin Portal (HR.Modules.Companies's
// GenerateSupportSession feature — POST /api/companies/admin/customers/{companyId}/support-session,
// platform:admin policy, requires a typed reason, 20-minute single-use token) and is given a link
// to this endpoint.
//
// STUB — DELIBERATE, NOT AN OVERSIGHT: this endpoint validates and consumes the token (via
// HR.Modules.Companies's RedeemSupportSession endpoint) and confirms/audits that redemption, but
// it does NOT establish an authenticated HR.Web session as the customer's company. Doing so safely
// requires either:
//   (a) a genuine Supabase Admin API-driven session mint for a real customer user (true user
//       impersonation) — a materially larger, higher-risk change to the auth surface, or
//   (b) teaching HR.Api's authorization pipeline (SupabaseCurrentUserResolutionMiddleware,
//       RequireTenantMiddleware, TenantRouteAuthorizationMiddleware — see HR.Modules.Identity) a
//       new "support-scoped, company-only, not-a-real-user" identity shape, which is itself a
//       security-sensitive change to code shared by every authenticated request in the system.
// Both are out of scope for this pass. Building either without a focused security review of that
// shared middleware would risk silently weakening authentication/authorization for every tenant,
// which is a materially worse outcome than shipping "the safe half" of this feature. See
// SupportSessionState's remarks for the same reasoning from the client-side half (the visible
// support banner), which is built and ready to be driven by whichever mechanism above is chosen.
app.MapGet("/support-session/redeem", async (
    HttpContext context,
    string? token,
    IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.Content("""
            <!DOCTYPE html>
            <html><head><title>Invalid support session link</title></head>
            <body><h1>Invalid support session link</h1>
            <p>No token was supplied.</p></body></html>
            """, "text/html");
    }

    var http = httpClientFactory.CreateClient("hrapi");

    HttpResponseMessage response;
    try
    {
        response = await http.PostAsJsonAsync(
            "api/companies/admin/support-session/redeem",
            new { Token = token },
            context.RequestAborted);
    }
    catch (HttpRequestException)
    {
        return Results.Content("""
            <!DOCTYPE html>
            <html><head><title>Support session error</title></head>
            <body><h1>Something went wrong</h1>
            <p>Could not reach the support-session service. Please try again.</p></body></html>
            """, "text/html");
    }

    if (!response.IsSuccessStatusCode)
    {
        return Results.Content("""
            <!DOCTYPE html>
            <html><head><title>Support session link expired or invalid</title></head>
            <body><h1>This support session link is no longer valid</h1>
            <p>It may have expired, already been used, or been revoked. Generate a new one from the
            Admin Portal.</p></body></html>
            """, "text/html");
    }

    // Token was valid and is now consumed (single-use — RedeemSupportSession marks it redeemed
    // server-side and this call cannot succeed a second time). The redemption itself is fully
    // real, audited, and functional; only the "establish an authenticated HR.Web session as this
    // customer" half is intentionally not implemented — see the remarks above this endpoint.
    return Results.Content($"""
        <!DOCTYPE html>
        <html><head><title>Support session validated</title></head>
        <body>
        <h1>Support session validated</h1>
        <p>The support session token was valid and has now been consumed. This confirms the
        platform administrator's access grant was genuine and has been recorded in the audit log.</p>
        <p><strong>Full automatic sign-in into the customer's environment is not implemented in
        this build.</strong> Establishing a real authenticated session safely requires a dedicated
        follow-up change to HR.Api's shared authentication/authorization middleware, which was
        deliberately deferred rather than rushed — see Program.cs's remarks on this endpoint for the
        full reasoning.</p>
        </body></html>
        """, "text/html");
}).AllowAnonymous();

// Authenticated proxy for downloading the employee import template (used by the Getting Started
// "Download the Employee import template" task — see DownloadEmployeeImportTemplateTask). A plain
// HTML <a href> can't attach a Supabase Bearer token to a call to hrapi directly, but it DOES
// automatically send this app's own session cookie on a same-origin request — this bridges that
// cookie auth to the real Bearer-authenticated hrapi call (via the "hrapi" HttpClient, which
// already attaches the token via SupabaseAuthDelegatingHandler/SupabaseSessionAccessor) and
// streams the file straight back with the same Content-Disposition the api endpoint itself sets,
// so the browser downloads it exactly as if the link pointed at a static file. Requires
// authentication via the default ("NoOp") scheme — same cookie-presence check every other
// [Authorize]'d Razor page in this app already relies on; the real permission check still happens
// server-side against hrapi's own "employee:manage" policy.
app.MapGet("/companies/{companyId:guid}/data-import/employees/template/download", async (
    Guid companyId,
    IHttpClientFactory httpClientFactory) =>
{
    var http = httpClientFactory.CreateClient("hrapi");
    using var response = await http.GetAsync($"api/companies/{companyId}/data-import/employees/template");

    if (!response.IsSuccessStatusCode)
    {
        return Results.StatusCode((int)response.StatusCode);
    }

    var bytes = await response.Content.ReadAsByteArrayAsync();
    var contentType = response.Content.Headers.ContentType?.ToString()
        ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
        ?? response.Content.Headers.ContentDisposition?.FileName
        ?? "employee-import-template.xlsx";

    // Best-effort: mark the "Download the Employee import template" onboarding task complete now
    // that the file has actually been streamed back successfully. Failure here must never block
    // or fail the download itself — the checklist item is a non-mandatory helper step.
    try
    {
        await http.PostAsync("api/company-onboarding/checklist/tasks/download-employee-import-template/mark-complete", null);
    }
    catch
    {
        // Swallow — the download already succeeded and is the primary outcome of this request.
    }

    return Results.File(bytes, contentType, fileName);
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
