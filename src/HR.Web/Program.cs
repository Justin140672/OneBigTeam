using HR.Web.Components;
using HR.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Syncfusion.Blazor;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Add services to the container.
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
// Attaches a real Supabase access token (once one has been established via /verify-email) as a
// Bearer token on every outgoing hrapi request — see SupabaseAuthDelegatingHandler/
// SupabaseSessionAccessor remarks. No-op for the existing Development dev-persona flow.
.AddHttpMessageHandler<SupabaseAuthDelegatingHandler>();

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
builder.Services.AddScoped<EmployeeNumberBackfillService>();
builder.Services.AddScoped<EmployeeNoteService>();
builder.Services.AddScoped<AuditHistoryService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<EmployeeTimelineService>();
builder.Services.AddScoped<OrganisationHierarchyBuilder>();
builder.Services.AddScoped<OrganisationChartService>();
builder.Services.AddScoped<SicknessService>();
builder.Services.AddScoped<DevAuthService>();
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
// the token itself — this endpoint does not need to, and never did trust it blindly), sets the
// HttpOnly session cookie, and redirects into the app.
app.MapGet("/verify-email-complete", async (
    HttpContext context,
    string? access_token,
    int? expires_in,
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

    // HttpOnly so the token is never exposed to client-side script. Also carried one hop further
    // via the URL below (not just the cookie) — see /dev/persona-cookie's remarks and Routes.razor
    // for why: Blazor Server's persistent circuit can survive this navigation with its own
    // SupabaseSessionAccessor instance still holding whatever it resolved BEFORE this cookie
    // existed, and IHttpContextAccessor.HttpContext is never reliably available again on that
    // circuit to re-read it.
    SupabaseSessionAccessor.SetSessionCookie(context, access_token, expires_in ?? 3600);

    var expiresInSeconds = expires_in ?? 3600;
    return Results.Redirect($"/getting-started?st={Uri.EscapeDataString(access_token)}&se={expiresInSeconds}");
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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
