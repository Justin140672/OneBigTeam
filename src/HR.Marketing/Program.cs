using HR.Marketing.Components;
using HR.Marketing.Models;
using HR.Marketing.Services;
using System.Security;
using System.Text;

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
})
// SocketsHttpHandler's default PooledConnectionLifetime is infinite, so a connection idle long
// enough (e.g. this client only calling out on an occasional signup/resend/contact submission)
// can be silently closed server-side by Kestrel's own keep-alive timeout while the pool still
// considers it valid — the next request reused from the pool then fails mid-flight with an
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

var app = builder.Build();

// Configure the HTTP request pipeline.
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

app.MapGet("/sitemap.xml", (HttpRequest request) =>
{
    var origin = $"{request.Scheme}://{request.Host}";
    var paths = new[]
    {
        "", "features", "pricing", "contact", "roadmap", "security", "privacy-policy",
        "subprocessors", "terms-of-service", "cookie-policy", "acceptable-use-policy",
        "data-processing-agreement"
    }.Concat(FeatureCatalog.All.Select(feature => $"features/{feature.Slug}"));

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

    // Round-trip everything except the password on a correctable error, so the visitor doesn't
    // have to retype the whole form (mirrors /contact-submit's retry-URL pattern below).
    string BuildRetryUrl(string errorMessage, bool existingEmail = false) =>
        "/signup?"
        + $"error={Uri.EscapeDataString(errorMessage)}"
        + (existingEmail ? "&existingEmail=true" : "")
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
        // 409 Conflict means an account/company already exists for this email — we don't reveal
        // which, to avoid leaking account existence, but we do point the visitor at login/password
        // recovery instead of leaving them stuck retrying signup with the same email.
        if (signUpResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Results.Redirect(BuildRetryUrl(
                "We couldn't create your account with those details.",
                existingEmail: true));
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
