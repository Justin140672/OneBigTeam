using HR.Marketing.Components;
using HR.Marketing.Models;
using HR.Marketing.Services;
using System.Security;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddRazorComponents();

builder.Services.Configure<PricingOptions>(builder.Configuration);
builder.Services.AddSingleton<IMarketingAnalytics, LoggingMarketingAnalytics>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<SubscriptionPricingProvider>();

builder.Services.AddSingleton<MarketingContentService>();

builder.Services.AddHttpClient("hrapi", c =>
{
    c.BaseAddress = new Uri(
        builder.Configuration["services:api:https:0"] ??
        builder.Configuration["services:api:http:0"] ??
        throw new InvalidOperationException("API base URL is missing. Expected services:api:https:0 or services:api:http:0."));
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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();

app.MapGet("/robots.txt", (HttpRequest request) =>
{
    var origin = $"{request.Scheme}://{request.Host}";
    return Results.Text($"User-agent: *\nAllow: /\n\nSitemap: {origin}/sitemap.xml\n", "text/plain");
});

app.MapGet("/sitemap.xml", async (HttpRequest request, MarketingContentService marketingContent) =>
{
    var origin = $"{request.Scheme}://{request.Host}";
    var content = await marketingContent.GetContentAsync(request.HttpContext.RequestAborted);
    var paths = new[]
    {
        "", "features", "pricing", "contact", "roadmap", "security", "privacy-policy",
        "subprocessors", "terms-of-service", "cookie-policy", "acceptable-use-policy",
        "data-processing-agreement"
    }.Concat(content.Features.Select(feature => $"features/{feature.Slug}"));

    var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
        .AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
    foreach (var path in paths)
    {
        xml.Append("  <url><loc>")
            .Append(SecurityElement.Escape($"{origin}/{path}"))
            .AppendLine("</loc></url>");
    }
    xml.AppendLine("</urlset>");
    return Results.Text(xml.ToString(), "application/xml");
});

app.MapGet("/coming-soon", () => Results.Redirect("/roadmap", permanent: true));
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

    string BuildRetryUrl(string errorMessage, bool existingEmail = false, string? emailError = null) =>
        "/signup?"
        + $"error={Uri.EscapeDataString(errorMessage)}"
        + (existingEmail ? "&existingEmail=true" : "")
        + (emailError is not null ? $"&emailError={Uri.EscapeDataString(emailError)}" : "")
        + $"&companyName={Uri.EscapeDataString(model.CompanyName)}"
        + $"&firstName={Uri.EscapeDataString(model.AdminFirstName)}"
        + $"&lastName={Uri.EscapeDataString(model.AdminLastName)}"
        + $"&email={Uri.EscapeDataString(model.AdminEmail)}";

    var http = httpClientFactory.CreateClient("hrapi");

    HttpResponseMessage signUpResponse;
    try
    {
        signUpResponse = await http.PostAsJsonAsync("api/signup", new
        {
            model.CompanyName,
            model.AdminFirstName,
            model.AdminLastName,
            model.AdminEmail,
            model.Password,
        });
    }
    catch (HttpRequestException)
    {
        return Results.Redirect(BuildRetryUrl("We couldn't reach our servers. Please try again shortly."));
    }

    if (!signUpResponse.IsSuccessStatusCode)
    {
        // 409 Conflict means an account already exists for this email. Say so plainly and point
        // the visitor at login/password recovery instead of leaving them stuck retrying signup.
        if (signUpResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Results.Redirect(BuildRetryUrl(
                "You already have an account with this email address.",
                existingEmail: true));
        }

        // Ticket 9: HR.Api's authoritative work-email policy rejected a public/disposable email
        // domain (400, code "work_email_required"). Surface the API's own message and flag the
        // email field so SignUp.razor can mark it invalid accessibly. Company name, admin name and
        // email are still round-tripped; the password never is.
        if (signUpResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var problem = await TryReadSignUpProblemAsync(signUpResponse);
            if (problem?.Code == SignUpProblem.WorkEmailRequiredCode && !string.IsNullOrWhiteSpace(problem.Error))
            {
                return Results.Redirect(BuildRetryUrl(problem.Error, emailError: SignUpProblem.WorkEmailRequiredCode));
            }
        }

        return Results.Redirect(BuildRetryUrl("We couldn't create your account. Please check your details and try again."));
    }

    var signUp = await signUpResponse.Content.ReadFromJsonAsync<StartTrialSignUpResult>();
    if (signUp is null)
    {
        return Results.Redirect(BuildRetryUrl("Something went wrong. Please try again."));
    }

    return Results.Redirect($"/check-your-email?email={Uri.EscapeDataString(signUp.Email)}");
});

app.MapPost("/resend-verification", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
{
    var form = await request.ReadFormAsync();
    var email = form["email"].ToString();

    var http = httpClientFactory.CreateClient("hrapi");
    await http.PostAsJsonAsync("api/resend-verification", new { Email = email });

    return Results.Redirect($"/check-your-email?email={Uri.EscapeDataString(email)}&resent=true");
});

app.MapPost("/contact-submit", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
{
    var form = await request.ReadFormAsync();
    var name = form["name"].ToString();
    var email = form["email"].ToString();
    var company = form["company"].ToString();
    var employeeCountRaw = form["employee-count"].ToString();
    var message = form["message"].ToString();
    var website = form["website"].ToString();

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

static async Task<SignUpProblem?> TryReadSignUpProblemAsync(HttpResponseMessage response)
{
    try
    {
        return await response.Content.ReadFromJsonAsync<SignUpProblem>();
    }
    catch (System.Text.Json.JsonException)
    {
        return null;
    }
    catch (NotSupportedException)
    {
        return null;
    }
}

internal sealed record StartTrialSignUpResult(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);

// Ticket 9: HR.Api's canonical error body ({ error, code } — see HR.SharedKernel.ProblemResults).
internal sealed record SignUpProblem(string? Error, string? Code)
{
    public const string WorkEmailRequiredCode = "work_email_required";
}
