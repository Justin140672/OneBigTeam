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
builder.Services.AddScoped<AppSession>();
builder.Services.AddScoped<AuthenticationStateProvider, AppSessionAuthStateProvider>();
builder.Services.AddAuthentication("NoOp")
    .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("NoOp", _ => { });
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
// point HttpContext is null and the token would be cached as missing for the whole circuit —
// silently falling back to no Authorization header on every hrapi call, which HR.Api's dev-mode
// dual auth scheme then treats as an anonymous/dev request rather than the real signed-in user.
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

    // HttpOnly so the token is never exposed to client-side script; read back only via
    // IHttpContextAccessor during the next request's SupabaseSessionAccessor construction (see
    // that class's remarks on why Blazor Server needs this rather than an in-memory-only session).
    context.Response.Cookies.Append(SupabaseSessionAccessor.CookieName, access_token, new CookieOptions
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UtcNow.AddSeconds(expires_in ?? 3600),
        Path = "/",
    });

    return Results.Redirect("/getting-started");
}).AllowAnonymous();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
