using System.Net;
using System.Threading.RateLimiting;
using FastEndpoints;
using HR.Api.Authentication;
using HR.Api.Startup;
using HR.Infrastructure;
using HR.Infrastructure.Logging;
using HR.Modules.Companies;
using HR.Modules.CompanyOnboarding;
using HR.Modules.DataImport;
using HR.Modules.Documents;
using HR.Modules.Employees;
using HR.Modules.Identity;
using HR.Modules.Leave;
using HR.Modules.Notifications;
using HR.Modules.Onboarding;
using HR.Modules.Offboarding;
using HR.Modules.Assets;
using HR.Modules.Sickness;
using HR.Modules.Probation;
using HR.Modules.Reporting;
using HR.Modules.Recruitment;
using HR.Modules.Support;
using HR.Modules.Tasks;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// Raises the .NET ThreadPool's minimum worker/IOCP thread counts above their (low, core-count-based)
// defaults. The default pool only grows one thread roughly every 500ms once demand exceeds the
// minimum, so a sudden burst of concurrent work — many parallel E2E Playwright sessions all hitting
// this API's async DB/HTTP-bound endpoints at once after being comparatively idle — can queue up
// faster than the pool ramps up, surfacing as request latency/timeouts that look like app overload
// even when CPU and DB connections both have headroom. This is a well-known ASP.NET Core scaling
// gotcha for bursty, I/O-bound workloads (see Microsoft's own "ThreadPool starvation" guidance) and
// is safe in every environment: it only raises the floor the pool starts warm at, never a ceiling.
ThreadPool.SetMinThreads(Environment.ProcessorCount * 12, Environment.ProcessorCount * 12);

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Host.UseSerilogWithDefaults();

var connectionString = builder.Configuration.GetConnectionString("hr")
	?? throw new InvalidOperationException("Connection string 'hr' was not found.");

// Npgsql's own defaults (Maximum Pool Size=100, Minimum Pool Size=0) mean the pool starts cold and
// grows lazily under load, then caps out at 100 real connections shared across every module's
// DbContext (they all use this same connection string, so Npgsql pools them together). Under this
// suite's concurrent E2E load that shared pool is one of several plausible contributors to the
// "shared Aspire-hosted app gets busy" timeouts already documented throughout the E2E test
// infrastructure (see E2ETestBase's own remarks) — raised here to rule it out / relieve it as a
// bottleneck: a higher ceiling and a small warm floor so connections don't need to be established
// from scratch on every burst. Harmless for a normal single-app-instance deployment against its own
// dedicated Postgres — this isn't shared across unrelated services.
var isE2ETestingRun = string.Equals(
	Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
// Under the E2E run this one api instance is shared across up to 15 concurrent Playwright circuits,
// so give the pool a higher ceiling and a warmer floor (the AppHost lifts Postgres' own
// max_connections to 500 to stay clear of this). A normal deployment keeps the more conservative
// 300/10 — plenty for a single app against its own dedicated Postgres.
connectionString += isE2ETestingRun
	? ";Maximum Pool Size=400;Minimum Pool Size=30"
	: ";Maximum Pool Size=300;Minimum Pool Size=10";

builder.Services.AddCompaniesModule(connectionString, builder.Configuration);
builder.Services.AddCompanyOnboardingModule(connectionString);
builder.Services.AddDataImportModule(connectionString, builder.Configuration);
builder.Services.AddDocumentsModule(connectionString, builder.Configuration);
builder.Services.AddEmployeesModule(connectionString, builder.Configuration);
builder.Services.AddIdentityModule(connectionString, builder.Configuration);
builder.Services.AddLeaveModule(connectionString);
builder.Services.AddNotificationsModule(connectionString);
builder.Services.AddOnboardingModule(connectionString);
builder.Services.AddOffboardingModule(connectionString);
builder.Services.AddTasksModule(connectionString);
builder.Services.AddProbationModule(connectionString);
builder.Services.AddRecruitmentModule(connectionString, builder.Configuration);
builder.Services.AddAssetsModule(connectionString);
builder.Services.AddSicknessModule(connectionString);
builder.Services.AddSupportModule(connectionString);
builder.Services.AddReportingModule(connectionString);
builder.Services.AddInfrastructure(connectionString, builder.Configuration);
builder.Services.AddHangfireBackgroundJobs(connectionString);
builder.Services.AddFastEndpoints(o => o.IncludeAbstractValidators = true);
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

// Lightweight abuse protection for the public marketing contact form (POST /api/contact below) —
// no external captcha service exists in this codebase, so a simple per-IP fixed window limiter is
// used instead. Keeps this endpoint from being used to spam the configured recipient inbox or hammer
// the Postmark API.
builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	// Window/PermitLimit are configurable (not just hardcoded) so integration tests can widen them —
	// ContactEndpointTests exercises many validation cases per class run against one in-memory
	// TestServer, all sharing a single per-IP partition; production defaults are unchanged.
	var contactFormRateLimitWindowMinutes = builder.Configuration.GetValue("Marketing:ContactForm:RateLimit:WindowMinutes", 5);
	var contactFormRateLimitPermitLimit = builder.Configuration.GetValue("Marketing:ContactForm:RateLimit:PermitLimit", 5);
	options.AddPolicy("contact-form", context =>
		RateLimitPartition.GetFixedWindowLimiter(
			partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			factory: _ => new FixedWindowRateLimiterOptions
			{
				Window = TimeSpan.FromMinutes(contactFormRateLimitWindowMinutes),
				PermitLimit = contactFormRateLimitPermitLimit,
				QueueLimit = 0,
			}));
});

// Supabase-backed JWT Bearer validation, shared between Development (dual-scheme below) and
// non-Development (sole scheme). This Supabase project uses asymmetric JWT signing (JWKS), not a
// shared HS256 secret — see SupabaseJwksKeyResolver below.
void ConfigureSupabaseJwtBearer(JwtBearerOptions options)
{
	var supabaseProjectUrl = builder.Configuration["SupabaseAuth:ProjectUrl"] ?? "";
	var supabaseJwksUrl = builder.Configuration["SupabaseAuth:JwksUrl"] ?? "";

	// Same E2E_TESTING flag that swaps in FakeSupabaseAuthGateway (HR.Modules.Identity.IdentityModule)
	// — read here too so this process's own JWT validation can accept the locally-signed tokens that
	// gateway now mints for E2E sign-in (see HR.Modules.Identity.Services.E2eFakeSupabaseJwt's remarks
	// for why: real Supabase Auth rate-limits sign-in under this suite's login volume). Both checks
	// read the exact same env var, so they can never disagree about which mode the process is in.
	var isE2ETesting = string.Equals(
		Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);

	// Without this, JwtBearerHandler silently remaps short JWT claim names to legacy long-form
	// ClaimTypes URIs (e.g. "sub" -> ClaimTypes.NameIdentifier, "email" -> ClaimTypes.Email) via
	// JwtSecurityTokenHandler's default inbound claim mapping. CurrentUserClaims/
	// SupabaseCurrentUserResolutionMiddleware look up "sub"/"email" literally, so without this
	// they'd never find them on a real Supabase-issued token — resolving to an authenticated-but-
	// unidentified user (UserId null), which fails every downstream check with a fast 403.
	options.MapInboundClaims = false;

	options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
	{
		ValidateIssuer = true,
		ValidIssuer = $"{supabaseProjectUrl}/auth/v1",
		ValidateAudience = true,
		// "authenticated" is Supabase's well-known audience claim value for authenticated users on
		// issued access tokens.
		ValidAudience = "authenticated",
		ValidateLifetime = true,
		// Only ever returns more than the real JWKS keys when E2E_TESTING=true — every other
		// environment (Development without that flag, and Production, which never sets it) resolves
		// signing keys exactly as before, unchanged. JwtBearerHandler tries every candidate key
		// returned here against the token's signature, so appending one extra, fixed, non-secret
		// symmetric key under E2E_TESTING doesn't weaken or alter validation against real
		// Supabase-issued tokens in any environment — it only additionally allows tokens actually
		// signed with that same key, which only HR.Modules.Identity.Services.E2eFakeSupabaseJwt
		// (itself only reachable via FakeSupabaseAuthGateway, itself only registered under this same
		// flag) ever mints.
		IssuerSigningKeyResolver = (_, _, kid, _) =>
		{
			// Under E2E_TESTING, every token actually presented to this API was minted locally by
			// E2eFakeSupabaseJwt (see FakeSupabaseAuthGateway) — it is never signed by the real
			// Supabase project, so a real key from SupabaseJwksKeyResolver could never match it
			// anyway. Skip the real JWKS fetch entirely in this mode: SupabaseJwksKeyResolver.GetKeySet
			// blocks the calling thread synchronously on an HttpClient.GetStringAsync(...).GetAwaiter()
			// .GetResult() call under a SemaphoreSlim.Wait() lock (IssuerSigningKeyResolver has no
			// async form), and the E2E environment's SupabaseAuth:JwksUrl target may be slow,
			// rate-limited, or unreachable — every authenticated request's JWT validation (i.e. every
			// page load past login) would otherwise pay that synchronous network cost once per 10-minute
			// cache window, which is consistent with the app shell repeatedly failing to load within
			// the E2E suite's 40-45s waits. Real (non-E2E) environments are completely unaffected —
			// this branch only ever runs when E2E_TESTING=true.
			if (isE2ETesting)
			{
				return [HR.Modules.Identity.Services.E2eFakeSupabaseJwt.SigningKey];
			}

			return SupabaseJwksKeyResolver.ResolveSigningKeys(supabaseJwksUrl, kid);
		},
	};

	// The default Serilog MinimumLevel.Override for "Microsoft" (Warning) swallows JwtBearerHandler's
	// own authentication-failure logs, so a validation failure otherwise surfaces only as a bare 401
	// with no indication of why (expired token, bad signature, issuer/audience mismatch, etc.).
	options.Events = new JwtBearerEvents
	{
		OnAuthenticationFailed = context =>
		{
			context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
				.CreateLogger("SupabaseJwtBearer")
				.LogWarning(context.Exception, "Supabase JWT validation failed");
			return Task.CompletedTask;
		},
	};
}

if (builder.Environment.IsDevelopment())
{
	builder.Services.AddSingleton<DevPersonaStore>();
}

// Supabase-backed authentication for all environments. Development previously fell back to a
// DevAuthHandler dev-persona auto-login bypass; that has been removed — Development now always
// authenticates through real Supabase, same as production (see the "Switch development to real
// Supabase auth" plan). The dev persona switcher in HR.Web still exists, but it now performs a
// real Supabase password-grant login (see /api/dev/persona/{userId} below) rather than flipping
// an in-memory claims pointer.
builder.Services
	.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(ConfigureSupabaseJwtBearer);

builder.Services
	.AddAuthorizationBuilder()
	.AddRolePolicies();

// A required migration failure must make the instance NOT ready (see StartupMigrationRunner).
builder.Services.AddSingleton<StartupMigrationRunner>();
builder.Services.AddHealthChecks()
	.AddCheck<StartupMigrationHealthCheck>(
		"startup-migrations",
		failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
		tags: ["ready", "critical"]);

var app = builder.Build();

var migrationRunner = app.Services.GetRequiredService<StartupMigrationRunner>();

// Required migrations + seeding, run in dependency order. Each step is awaited in sequence; a
// failure records the affected module and (below) prevents the normal request pipeline and the
// Hangfire recurring job registration from being wired up.
await migrationRunner.RunAsync("companies", app.Services, async sp =>
{
	await sp.MigrateCompaniesAsync();
	await sp.SeedCompaniesAsync();
});

await migrationRunner.RunAsync("companyOnboarding", app.Services, async sp =>
{
	await sp.MigrateCompanyOnboardingAsync();
	await sp.SeedCompanyOnboardingAsync();
});

await migrationRunner.RunAsync("dataImport", app.Services, sp => sp.MigrateDataImportAsync());

await migrationRunner.RunAsync("employees", app.Services, async sp =>
{
	await sp.MigrateEmployeesAsync();
	// The E2E arrange-data pool is only ever wanted for the Playwright E2E run (E2E_TESTING=true,
	// the same flag that swaps in the fake Supabase/JWKS plumbing). It must NOT land in the
	// integration test DB (WebApplicationFactory<Program> also runs as Development) nor in any
	// real environment.
	var seedE2eTestPool = string.Equals(
		Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
	await sp.SeedEmployeesAsync(includeE2eTestPool: seedE2eTestPool);
});

await migrationRunner.RunAsync("identity", app.Services, async sp =>
{
	await sp.MigrateIdentityAsync();
	// Bootstrap PlatformAdministrator rows from the PlatformAdmin:AllowedEmails config allow-list.
	// Runs in every environment (the allow-list itself is configured per-environment). Idempotent.
	await sp.SeedPlatformAdministratorsFromConfigAsync(app.Configuration);
	// IAM-03: idempotent, additive-only backfill of position-based role assignments.
	await sp.ReconcilePositionRoleAssignmentsAsync();
	if (app.Environment.IsDevelopment())
	{
		await sp.SeedDevUserAsync();
		await sp.SeedDevSupabaseUsersAsync(DevPersonaStore.Personas.Select(p =>
		{
			var nameParts = p.Name.Split(' ', 2);
			return (
				Id: Guid.Parse(p.UserId),
				CompanyId: Guid.Parse(p.CompanyId),
				Email: p.Email,
				FirstName: nameParts[0],
				LastName: nameParts.Length > 1 ? nameParts[1] : "");
		}));
	}
});

await migrationRunner.RunAsync("audit", app.Services, sp => sp.MigrateAuditAsync());

await migrationRunner.RunAsync("documents", app.Services, async sp =>
{
	await sp.MigrateDocumentsAsync();
	await sp.SeedDocumentsAsync();
});

await migrationRunner.RunAsync("leave", app.Services, async sp =>
{
	await sp.MigrateLeaveAsync();
	await sp.SeedLeaveAsync();
});

await migrationRunner.RunAsync("notifications", app.Services, async sp =>
{
	await sp.MigrateNotificationsAsync();
	await sp.SeedNotificationsAsync();
});

await migrationRunner.RunAsync("tasks", app.Services, async sp =>
{
	await sp.MigrateTasksAsync();
	await sp.SeedTasksAsync();
});

await migrationRunner.RunAsync("onboarding", app.Services, async sp =>
{
	await sp.MigrateOnboardingAsync();
	if (string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase))
	{
		// E2E-only: every E2E arrange-data pool employee gets a NotStarted onboarding plan + the
		// 3 default checklist tasks. All Acme, StartDate 2026-03-01.
		var acmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
		var onboardingPoolStart = new DateOnly(2026, 3, 1);
		await sp.SeedE2eOnboardingPlansAsync(
			HR.Modules.Employees.EmployeesModule.E2eTestPool.Select(p => (
				acmeCompanyId, p.Id, onboardingPoolStart, $"E2E {p.LastName}")));
	}
});

await migrationRunner.RunAsync("offboarding", app.Services, sp => sp.MigrateOffboardingAsync());

await migrationRunner.RunAsync("probation", app.Services, async sp =>
{
	await sp.MigrateProbationAsync();
	await sp.SeedProbationAsync();
});

await migrationRunner.RunAsync("reporting", app.Services, sp => sp.MigrateReportingAsync());

await migrationRunner.RunAsync("assets", app.Services, async sp =>
{
	await sp.MigrateAssetsAsync();
	await sp.SeedAssetsAsync();
});

await migrationRunner.RunAsync("sickness", app.Services, async sp =>
{
	await sp.MigrateSicknessAsync();
	await sp.SeedSicknessAsync();
});

await migrationRunner.RunAsync("support", app.Services, async sp =>
{
	await sp.MigrateSupportAsync();
	await sp.SeedSupportAsync();
});

await migrationRunner.RunAsync("recruitment", app.Services, async sp =>
{
	await sp.MigrateRecruitmentAsync();
	await sp.SeedRecruitmentAsync();
});

app.MapGet("/health/startup-migrations", () => migrationRunner.ToHealthResult());

// OBT-REM-01: a required migration failure must NOT let the API serve normal traffic or register
// Hangfire recurring jobs. The process stays up in a non-ready state (health endpoints only) so
// orchestrators see 503 on /health/ready and do not route traffic here.
if (!migrationRunner.AllSucceeded)
{
	app.Logger.LogCritical(
		"API startup incomplete: required database migration(s) failed for {FailedModules}. "
		+ "Serving health endpoints only — normal traffic and background job registration are disabled.",
		string.Join(", ", migrationRunner.FailedModules));

	app.UseLoggingMiddleware();
	app.UseRouting();
	app.MapDefaultEndpoints();
	app.Run();
	return;
}

// Ticket 9 — operational safety for sensitive-data encryption. In every non-Development environment
// (staging, production) encryption MUST be fully configured before this instance serves traffic:
// special-category equality-monitoring data is stored as ciphertext and the keys — supplied only via
// environment/secret config at Infrastructure:SensitiveDataProtection:Keys — are the only thing
// standing between a database dump and readable data. A missing/invalid key set here is a deliberate
// hard startup crash (far safer than starting up and later discovering encrypted data is unreadable,
// or silently persisting plaintext). AesGcmSensitiveDataProtector.Create never generates a
// replacement key, and the thrown exception never contains key material. Development (including the
// integration test host) keeps the lazy behaviour so environments without protected data are not
// forced to configure keys.
if (!app.Environment.IsDevelopment())
{
	app.Services.ValidateSensitiveDataProtectionOrThrow();
}

if (app.Environment.IsDevelopment())
{
	// AllPersonas (seeded catalog + runtime-registered self-service signups), not the static
	// Personas field alone — the login form's persona lookup (Login.razor) needs to find a
	// brand-new signup admin registered via /api/dev/persona/register, which only ever lands in
	// the instance's _registeredPersonas list, never in the static seeded catalog.
	app.MapGet("/api/dev/personas", (DevPersonaStore store) => store.AllPersonas.ToList()).AllowAnonymous();
	app.MapPost("/api/dev/persona/{userId}", async (string userId, DevPersonaStore store, IServiceProvider services) =>
	{
		// The dev persona switcher is the only real "sign-in" path in this codebase today (see
		// HR.Modules.Identity.IdentityModule.TryDevSignInAsync remarks) — this is where the
		// IsActive gate (ticket #88) and LastLoginAt recording (ticket #89) are wired in. After the
		// gate passes, a real Supabase password-grant login is performed for that persona's email
		// so HR.Web can establish a genuine Supabase session cookie (see the "Switch development to
		// real Supabase auth" plan).
		if (!Guid.TryParse(userId, out var userGuid))
			return Results.NoContent();

		var isAllowed = await services.TryDevSignInAsync(userGuid);
		if (!isAllowed)
			return Results.StatusCode(StatusCodes.Status403Forbidden);

		var persona = store.FindPersona(userId);
		if (persona is null)
			return Results.NotFound();

		var session = await services.SignInDevPersonaAsync(persona.Email, CancellationToken.None);
		return Results.Ok(new
		{
			accessToken = session.AccessToken,
			refreshToken = session.RefreshToken,
			expiresIn = session.ExpiresIn,
		});
	}).AllowAnonymous();

	// Establishes a dev-stub session for a brand-new self-service signup admin (HR.Modules.Identity's
	// SignUp feature returns exactly these fields). Identity cannot reference DevPersonaStore itself
	// (it lives in HR.Api, the host) — the client (marketing StartTrial page / HR.Web) calls this
	// immediately after a successful signup. The new admin is also seeded as a real Supabase dev
	// user and logged in via password grant, so the "auto-login after signup" UX keeps working under
	// real Supabase auth.
	app.MapPost("/api/dev/persona/register", async (
		RegisterDevPersonaRequest request, DevPersonaStore store, IServiceProvider services) =>
	{
		store.Register(new DevPersona(
			request.UserId.ToString(),
			request.CompanyId.ToString(),
			$"{request.FirstName} {request.LastName}".Trim(),
			"Company Administrator",
			request.Email));

		await services.EnsureDevSupabaseUserAsync(
			request.UserId, request.CompanyId, request.Email,
			request.FirstName, request.LastName, CancellationToken.None);
		var session = await services.SignInDevPersonaAsync(request.Email, CancellationToken.None);
		return Results.Ok(new
		{
			accessToken = session.AccessToken,
			refreshToken = session.RefreshToken,
			expiresIn = session.ExpiresIn,
		});
	}).AllowAnonymous();

	// Every Local*StorageService (used whenever the corresponding Supabase config section is
	// absent — the default in this dev environment) writes under one of these folders and used
	// to hand back a raw file:// path, which a browser refuses to load in an <img> tag or follow
	// via a redirect-based download endpoint. This streams the same local files back over HTTP
	// instead. "bucket" identifies which Local*StorageService's root to serve from.
	var localStorageBuckets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["profile-photos"]      = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "onebigteam", "profile-photos")),
		["documents"]            = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "onebigteam", "documents")),
		["candidate-documents"] = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "onebigteam", "recruitment", "candidate-documents")),
		["support-attachments"] = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "onebigteam", "support-attachments")),
	};
	var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();

	app.MapGet("/api/dev/local-storage/{bucket}/{*key}", (string bucket, string key) =>
	{
		if (!localStorageBuckets.TryGetValue(bucket, out var basePath))
			return Results.NotFound();

		var relativePath = string.Join(
			Path.DirectorySeparatorChar,
			key.Split('/').Select(Uri.UnescapeDataString));
		var fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));

		// Guard against the resolved path escaping the storage root (path traversal via "..").
		if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
			|| !File.Exists(fullPath))
		{
			return Results.NotFound();
		}

		if (!contentTypeProvider.TryGetContentType(fullPath, out var contentType))
			contentType = "application/octet-stream";

		return Results.File(fullPath, contentType);
	}).AllowAnonymous();
}

app.UseHangfireBackgroundJobs();
app.UseEmployeesRecurringJobs();
app.UseIdentityRecurringJobs();
app.UseProbationRecurringJobs();
app.UseAssetsRecurringJobs();
app.UseSicknessRecurringJobs();
app.UseSupportRecurringJobs();
app.UseRecruitmentRecurringJobs();
app.UseOnboardingRecurringJobs();
app.UseOffboardingRecurringJobs();
app.UseDocumentsRecurringJobs();
app.UseLeaveRecurringJobs();
app.UseReportingRecurringJobs();
app.UseNotificationsRecurringJobs();
app.UseLoggingMiddleware();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseIdentityModule();
app.UseAuthorization();
app.UseCompaniesModule();

// Public (anonymous) endpoint backing the marketing site's contact form (HR.Marketing's
// Contact.razor posts to its own /contact-submit proxy, which calls this). This intentionally does
// not live inside a business module: it has no company_id/tenant, persists nothing, and is a pure
// relay to Postmark via IEmailSender (already Infrastructure-owned per the deployment architecture
// spec — "Business modules never send emails directly"). Kept here in the host alongside the other
// small ad hoc endpoints above (dev personas, local storage) rather than inventing a new module.
app.MapPost("/api/contact", async (
	ContactRequest request,
	IEmailSender emailSender,
	IConfiguration configuration,
	ILogger<Program> logger,
	CancellationToken cancellationToken) =>
{
	var errors = ValidateContactRequest(request);
	if (errors.Count > 0)
	{
		logger.LogWarning("Contact form submission failed: {ErrorType}", "validation");
		return Results.ValidationProblem(errors);
	}

	// Honeypot: a real visitor never populates this hidden field. Bots that blindly fill every
	// field will. Report success (so the bot doesn't learn to avoid the field) without sending
	// anything or touching Postmark.
	if (!string.IsNullOrWhiteSpace(request.Website))
	{
		logger.LogInformation("Contact form submission discarded: {ErrorType}", "honeypot");
		return Results.Ok();
	}

	var recipient = configuration["Marketing:ContactForm:RecipientEmail"];
	if (string.IsNullOrWhiteSpace(recipient))
	{
		logger.LogWarning("Contact form submission failed: {ErrorType}", "recipient_not_configured");
		return Results.Problem("The contact form is not available right now. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
	}

	var companyDisplay = string.IsNullOrWhiteSpace(request.Company) ? "(not provided)" : request.Company.Trim();
	var employeeCountDisplay = request.EmployeeCount?.ToString() ?? "(not provided)";
	var subject = $"New contact form enquiry from {WebUtility.HtmlEncode(companyDisplay)}";
	var htmlBody = $"""
		<p><strong>Name:</strong> {WebUtility.HtmlEncode(request.Name.Trim())}</p>
		<p><strong>Email:</strong> {WebUtility.HtmlEncode(request.Email.Trim())}</p>
		<p><strong>Company:</strong> {WebUtility.HtmlEncode(companyDisplay)}</p>
		<p><strong>Approximate employee count:</strong> {WebUtility.HtmlEncode(employeeCountDisplay)}</p>
		<p><strong>Message:</strong></p>
		<p>{WebUtility.HtmlEncode(request.Message.Trim()).Replace("\n", "<br>")}</p>
		""";

	try
	{
		await emailSender.SendAsync(recipient, subject, htmlBody, cancellationToken);
	}
	catch (Exception ex)
	{
		// Never log the message body, email address, or other submitted PII — only the outcome and
		// exception type, per the coding standards' logging rules.
		logger.LogError(ex, "Contact form submission failed: {ErrorType}", ex.GetType().Name);
		return Results.Problem("We couldn't send your message. Please try again shortly.", statusCode: StatusCodes.Status502BadGateway);
	}

	logger.LogInformation("Contact form submission succeeded");
	return Results.Ok();
}).AllowAnonymous().RequireRateLimiting("contact-form");

static Dictionary<string, string[]> ValidateContactRequest(ContactRequest request)
{
	var errors = new Dictionary<string, string[]>();

	if (string.IsNullOrWhiteSpace(request.Name))
		errors["name"] = ["Enter your name."];
	else if (request.Name.Trim().Length > 200)
		errors["name"] = ["Name must be 200 characters or fewer."];

	if (string.IsNullOrWhiteSpace(request.Email))
	{
		errors["email"] = ["Enter your email address."];
	}
	else
	{
		try
		{
			_ = new System.Net.Mail.MailAddress(request.Email.Trim());
		}
		catch (FormatException)
		{
			errors["email"] = ["Enter a valid email address."];
		}
	}

	if (!string.IsNullOrWhiteSpace(request.Company) && request.Company.Trim().Length > 200)
		errors["company"] = ["Company name must be 200 characters or fewer."];

	if (request.EmployeeCount is < 1)
		errors["employeeCount"] = ["Enter an employee count of 1 or more."];

	if (string.IsNullOrWhiteSpace(request.Message))
		errors["message"] = ["Enter a short message."];
	else if (request.Message.Trim().Length > 4000)
		errors["message"] = ["Message must be 4000 characters or fewer."];

	return errors;
}
app.UseFastEndpoints(c =>
{
	c.Serializer.Options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
	c.Errors.StatusCode = 422;
});
app.MapDefaultEndpoints();

app.Run();

public partial class Program;

internal sealed record RegisterDevPersonaRequest(Guid UserId, Guid CompanyId, string FirstName, string LastName, string Email);

// "Website" is the honeypot field — must stay named plausibly enough that a scripted bot fills it,
// while a real visitor never sees or fills it (hidden via CSS in Contact.razor, not via type="hidden",
// so autofill/accessibility tooling still treats it as a normal-looking field bots target).
internal sealed record ContactRequest(
	string Name,
	string Email,
	string Company,
	int? EmployeeCount,
	string Message,
	string? Website);

// Fetches and caches Supabase's JWKS (bare JSON Web Key Set, RFC 7517) document so JWT Bearer
// validation can resolve the signing key matching a token's "kid" header. AddJwtBearer's built-in
// options.MetadataAddress auto-discovery expects a full OpenID Connect discovery document (which
// itself points at a jwks_uri) — Supabase's JwksUrl here is a bare JWKS document, not an OIDC
// discovery document, so MetadataAddress is not usable and this manual resolver is used instead.
// IssuerSigningKeyResolver is a synchronous callback, so the JWKS fetch below is synchronous
// (blocking) with a short in-memory cache to avoid fetching on every request.
internal static class SupabaseJwksKeyResolver
{
	private static readonly HttpClient HttpClient = new();
	private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
	private static readonly SemaphoreSlim RefreshLock = new(1, 1);

	private static Microsoft.IdentityModel.Tokens.JsonWebKeySet? _cachedKeySet;
	private static DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

	public static IEnumerable<Microsoft.IdentityModel.Tokens.SecurityKey> ResolveSigningKeys(string jwksUrl, string? kid)
	{
		var keySet = GetKeySet(jwksUrl);
		var keys = keySet.GetSigningKeys();

		return string.IsNullOrEmpty(kid)
			? keys
			: keys.Where(key => key.KeyId == kid);
	}

	private static Microsoft.IdentityModel.Tokens.JsonWebKeySet GetKeySet(string jwksUrl)
	{
		if (_cachedKeySet is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
			return _cachedKeySet;

		RefreshLock.Wait();
		try
		{
			if (_cachedKeySet is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
				return _cachedKeySet;

			// Blocking call: IssuerSigningKeyResolver is a synchronous delegate in
			// Microsoft.IdentityModel.Tokens, so there is no async alternative here.
			var json = HttpClient.GetStringAsync(jwksUrl).GetAwaiter().GetResult();
			_cachedKeySet = new Microsoft.IdentityModel.Tokens.JsonWebKeySet(json);
			_cachedAt = DateTimeOffset.UtcNow;
			return _cachedKeySet;
		}
		finally
		{
			RefreshLock.Release();
		}
	}
}
