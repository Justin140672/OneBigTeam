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

builder.Services.AddHttpClient("hrapi", c =>
{
    var apiBaseUrl =
        builder.Configuration["services:api:https:0"] ??
        builder.Configuration["services:api:http:0"] ??
        throw new InvalidOperationException("API base URL is missing. Expected services:api:https:0 or services:api:http:0.");

    c.BaseAddress = new Uri(apiBaseUrl);
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
builder.Services.AddScoped<CompensationService>();
builder.Services.AddScoped<AuditHistoryService>();
builder.Services.AddScoped<SicknessService>();
builder.Services.AddScoped<DevAuthService>();
builder.Services.AddScoped<VacancyService>();
builder.Services.AddScoped<CandidateService>();
builder.Services.AddScoped<ApplicationService>();
builder.Services.AddScoped<InterviewService>();
builder.Services.AddScoped<DataImportService>();
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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
