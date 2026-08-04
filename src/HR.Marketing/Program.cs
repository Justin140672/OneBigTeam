using HR.Marketing.Components;
using HR.Marketing.Models;
using HR.Marketing.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents();

builder.Services.Configure<PricingOptions>(builder.Configuration);
builder.Services.AddSingleton<IMarketingAnalytics, LoggingMarketingAnalytics>();

// Named client for the server-side "Start free trial" signup proxy (SignUp.razor) — calls
// HR.Api directly from the server, so the browser never needs cross-origin access to the API.
builder.Services.AddHttpClient("hrapi", c =>
{
    c.BaseAddress = new Uri(
        builder.Configuration["services:api:https:0"] ??
        builder.Configuration["services:api:http:0"] ??
        throw new InvalidOperationException("API base URL is missing. Expected services:api:https:0 or services:api:http:0."));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();
app.MapDefaultEndpoints();

// Server-side proxy for the "Start free trial" form (SignUp.razor) — a plain HTML <form>
// post (this Blazor app renders statically, no interactive circuit), so this is a conventional
// minimal API endpoint rather than a Blazor event handler. Calls HR.Api's public /api/signup
// endpoint, then establishes a dev-stub session (see HR.Api's /api/dev/persona/register remarks)
// and redirects the browser straight into HR.Web's "/getting-started", landing the new admin in
// an already-"signed-in" app — matching the plan's auto-login UX without introducing real
// Supabase Auth in this epic.
app.MapPost("/signup-submit", async (HttpRequest request, IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var form = await request.ReadFormAsync();
    var model = new CreateCompanyModel
    {
        CompanyName = form["companyName"].ToString(),
        AdminFirstName = form["firstName"].ToString(),
        AdminLastName = form["lastName"].ToString(),
        AdminEmail = form["email"].ToString(),
        Password = form["password"].ToString(),
    };

    var webBaseUrl =
        configuration["services:web:https:0"] ??
        configuration["services:web:http:0"] ??
        "http://localhost:5270";

    var http = httpClientFactory.CreateClient("hrapi");

    var signUpResponse = await http.PostAsJsonAsync("api/signup", new
    {
        model.CompanyName,
        model.AdminFirstName,
        model.AdminLastName,
        model.AdminEmail,
        model.Password,
    });

    if (!signUpResponse.IsSuccessStatusCode)
    {
        var errorMessage = signUpResponse.StatusCode == System.Net.HttpStatusCode.Conflict
            ? "An account with this email already exists."
            : "We couldn't create your account. Please check your details and try again.";
        return Results.Redirect($"/signup?error={Uri.EscapeDataString(errorMessage)}");
    }

    var signUp = await signUpResponse.Content.ReadFromJsonAsync<StartTrialSignUpResult>();
    if (signUp is null)
    {
        return Results.Redirect("/signup?error=" + Uri.EscapeDataString("Something went wrong. Please try again."));
    }

    await http.PostAsJsonAsync("api/dev/persona/register", new
    {
        signUp.UserId,
        signUp.CompanyId,
        signUp.FirstName,
        signUp.LastName,
        signUp.Email,
    });

    return Results.Redirect($"{webBaseUrl.TrimEnd('/')}/getting-started");
});

app.Run();

internal sealed record StartTrialSignUpResult(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);
