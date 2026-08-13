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
// endpoint. The admin is deliberately NOT auto-logged-in here: /api/signup now creates a real,
// pending Supabase Auth user (no session/token yet), so on success we redirect to the
// check-your-email page instead of establishing a session and jumping into HR.Web. The real
// sign-in only happens once the admin clicks the verification email link (Phase D's
// /verify-email flow in HR.Web).
app.MapPost("/signup-submit", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
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

    return Results.Redirect($"/check-your-email?email={Uri.EscapeDataString(signUp.Email)}");
});

// Server-side proxy for the "Resend verification email" button on CheckYourEmail.razor. Mirrors
// /signup-submit's shape: reads the posted form, calls HR.Api's public /api/resend-verification
// endpoint (which never leaks whether the email is actually registered), then redirects back to
// the check-your-email page with a "resent" flag so the page can show a brief confirmation.
app.MapPost("/resend-verification", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
{
    var form = await request.ReadFormAsync();
    var email = form["email"].ToString();

    var http = httpClientFactory.CreateClient("hrapi");
    await http.PostAsJsonAsync("api/resend-verification", new { Email = email });

    return Results.Redirect($"/check-your-email?email={Uri.EscapeDataString(email)}&resent=true");
});

// Server-side proxy for the contact form (Contact.razor) — same shape as /signup-submit: a plain
// HTML <form> post (this app renders statically, no interactive circuit) forwarded to HR.Api's
// public /api/contact endpoint, which relays the enquiry to Postmark. On failure, the originally
// entered values are round-tripped back via query string so the visitor doesn't have to retype
// everything.
app.MapPost("/contact-submit", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
{
    var form = await request.ReadFormAsync();
    var name = form["name"].ToString();
    var email = form["email"].ToString();
    var company = form["company"].ToString();
    var employeeCountRaw = form["employee-count"].ToString();
    var message = form["message"].ToString();
    var website = form["website"].ToString(); // honeypot — real visitors never populate this

    int? employeeCount = int.TryParse(employeeCountRaw, out var parsedCount) ? parsedCount : null;

    string BuildRetryUrl(string errorMessage) =>
        "/contact?status=error"
        + $"&error={Uri.EscapeDataString(errorMessage)}"
        + $"&name={Uri.EscapeDataString(name)}"
        + $"&email={Uri.EscapeDataString(email)}"
        + $"&company={Uri.EscapeDataString(company)}"
        + $"&employeeCount={Uri.EscapeDataString(employeeCountRaw)}"
        + $"&message={Uri.EscapeDataString(message)}"
        + "#contact-form";

    var http = httpClientFactory.CreateClient("hrapi");

    HttpResponseMessage contactResponse;
    try
    {
        contactResponse = await http.PostAsJsonAsync("api/contact", new
        {
            Name = name,
            Email = email,
            Company = company,
            EmployeeCount = employeeCount,
            Message = message,
            Website = website,
        });
    }
    catch (HttpRequestException)
    {
        return Results.Redirect(BuildRetryUrl("We couldn't send your message. Please try again shortly."));
    }

    if (!contactResponse.IsSuccessStatusCode)
    {
        var errorMessage = contactResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            ? "Too many messages sent recently. Please wait a few minutes and try again."
            : "We couldn't send your message. Please check your details and try again.";
        return Results.Redirect(BuildRetryUrl(errorMessage));
    }

    return Results.Redirect("/contact?status=success#contact-form");
});

app.Run();

internal sealed record StartTrialSignUpResult(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);
