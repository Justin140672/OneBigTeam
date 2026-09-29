using System.Net;
using System.Threading.RateLimiting;
using FastEndpoints;
using HR.Api.Authentication;
using HR.Api.RateLimiting;
using HR.Api.Startup;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure;
using HR.Infrastructure.Logging;
using HR.Modules.Companies;
using HR.Modules.CompanyOnboarding;
using HR.Modules.DataImport;
using HR.Modules.Documents;
using HR.Modules.Employees;
using HR.Modules.Identity;
using HR.Modules.Leave;
using HR.Modules.Marketing;
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

var isE2ETestingRun = string.Equals(
	Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);

// Ticket 1 — block production test authentication. E2E_TESTING swaps in fake Supabase auth, a
// non-secret JWT signing key and other test doubles (see IdentityModule / ConfigureSupabaseJwtBearer
// below). That plumbing must never be reachable in a real deployment, so fail fast at startup:
// the flag is only ever legitimately set by the local/CI E2E fixture, which runs as Development.
if (isE2ETestingRun && !builder.Environment.IsDevelopment())
{
	throw new InvalidOperationException(
		$"E2E_TESTING=true is not permitted in the '{builder.Environment.EnvironmentName}' environment. "
		+ "Test authentication is only allowed under Development. Refusing to start.");
}
connectionString += isE2ETestingRun
	? ";Maximum Pool Size=400;Minimum Pool Size=30"
	: ";Maximum Pool Size=300;Minimum Pool Size=10";

var devToolsOptions = new DevToolsOptions();
builder.Configuration.GetSection(DevToolsOptions.SectionName).Bind(devToolsOptions);
if (devToolsOptions.Enabled && !builder.Environment.IsDevelopment())
{
	throw new InvalidOperationException(
		$"DevTools.Enabled=true is not permitted in the '{builder.Environment.EnvironmentName}' environment. "
		+ "Development tools are only allowed under Development. Refusing to start.");
}

builder.Services.Configure<DevToolsOptions>(builder.Configuration.GetSection(DevToolsOptions.SectionName));

builder.Services.AddCompaniesModule(connectionString, builder.Configuration);
builder.Services.AddCompanyOnboardingModule(connectionString);
builder.Services.AddDataImportModule(connectionString, builder.Configuration, builder.Environment);
builder.Services.AddDocumentsModule(connectionString, builder.Configuration, builder.Environment);
builder.Services.AddEmployeesModule(connectionString, builder.Configuration);
builder.Services.AddIdentityModule(connectionString, builder.Configuration);
builder.Services.AddLeaveModule(connectionString);
builder.Services.AddMarketingModule(connectionString);
builder.Services.AddNotificationsModule(connectionString, builder.Configuration);
builder.Services.AddOnboardingModule(connectionString);
builder.Services.AddOffboardingModule(connectionString);
builder.Services.AddTasksModule(connectionString);
builder.Services.AddProbationModule(connectionString);
builder.Services.AddRecruitmentModule(connectionString, builder.Configuration, builder.Environment);
builder.Services.AddAssetsModule(connectionString);
builder.Services.AddSicknessModule(connectionString);
builder.Services.AddSupportModule(connectionString);
builder.Services.AddReportingModule(connectionString);
builder.Services.AddInfrastructure(connectionString, builder.Configuration, builder.Environment);
builder.Services.AddHangfireBackgroundJobs(connectionString);
builder.Services.AddFastEndpoints(o => o.IncludeAbstractValidators = true);
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<IClock, SystemClock>();
// Ticket 3 (P1) final gap item 5: production default is a no-op. An integration test overrides this
// registration (WebApplicationFactory ConfigureTestServices) with a fault-injecting double to
// reproduce "committed, then the response failed" without any production code depending on test
// infrastructure. See IPostCommitFaultInjector's remarks.
builder.Services.AddSingleton<HR.SharedKernel.Idempotency.IPostCommitFaultInjector,
    HR.SharedKernel.Idempotency.NoOpPostCommitFaultInjector>();
builder.Services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	var contactFormRateLimitWindowMinutes = builder.Configuration.GetValue("Marketing:ContactForm:RateLimit:WindowMinutes", 5);
	var contactFormRateLimitPermitLimit = builder.Configuration.GetValue("Marketing:ContactForm:RateLimit:PermitLimit", 5);
	options.AddPolicy(RateLimitRejectionLogging.ContactFormPolicy, context =>
		RateLimitPartition.GetFixedWindowLimiter(
			partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			factory: _ => new FixedWindowRateLimiterOptions
			{
				Window = TimeSpan.FromMinutes(contactFormRateLimitWindowMinutes),
				PermitLimit = contactFormRateLimitPermitLimit,
				QueueLimit = 0,
			}));

	options.AddIdentityRateLimiting(builder.Configuration);
});

builder.Services.ConfigureTrustedProxies(builder.Configuration);

// Supabase-backed JWT Bearer validation. This Supabase project uses asymmetric JWT signing
// (ES256/RS256 via a bare JWKS document) — Ticket 6 replaced the hand-rolled synchronous JWKS
// resolver with an async ConfigurationManager<OpenIdConnectConfiguration>. All wiring lives in
// HR.Api.Authentication.SupabaseJwtBearerConfiguration so the real pipeline can be exercised verbatim
// by SigningKeyRefreshResilienceTests. The ConfigurationManager is attached in a second, DI-aware
// options configuration below so it can log retrieval failures through ILoggerFactory.
//
// The E2E_TESTING flag is the same one that swaps in FakeSupabaseAuthGateway
// (HR.Modules.Identity.IdentityModule): under it this process validates locally-signed HS256 tokens
// and never touches the network.
void ConfigureSupabaseJwtBearer(JwtBearerOptions options)
{
	var isE2ETesting = string.Equals(
		Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);

	SupabaseJwtBearerConfiguration.ConfigureValidation(options, builder.Configuration, isE2ETesting);
}

if (builder.Environment.IsDevelopment())
{
	builder.Services.AddSingleton<DevPersonaStore>();
}

const string AuthenticationSelectorScheme = "BearerOrSupportSession";

builder.Services
	.AddAuthentication(AuthenticationSelectorScheme)
	.AddPolicyScheme(AuthenticationSelectorScheme, "Supabase Bearer or Support Session", selectorOptions =>
	{
		selectorOptions.ForwardDefaultSelector = context =>
		{
			var header = context.Request.Headers.Authorization.ToString();
			if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			{
				var token = header["Bearer ".Length..].Trim();
				if (HR.Api.Authentication.SupportSessionJwtBearerConfiguration.LooksLikeSupportSessionToken(token))
					return HR.Api.Authentication.SupportSessionJwtBearerConfiguration.SchemeName;
			}

			return JwtBearerDefaults.AuthenticationScheme;
		};
	})
	.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, ConfigureSupabaseJwtBearer)
	.AddJwtBearer(
		HR.Api.Authentication.SupportSessionJwtBearerConfiguration.SchemeName,
		options => HR.Api.Authentication.SupportSessionJwtBearerConfiguration.ConfigureValidation(options, builder.Configuration));

// Ticket 6: attach the async ConfigurationManager<OpenIdConnectConfiguration> for the real Supabase
// JWKS endpoint, wrapped in a FreshnessGatedConfigurationManager that enforces an absolute
// MaximumCachedKeyAge on every key-supplying path (IdentityModel's own LastKnownGoodLifetime only
// bounds the LKG fallback slot and never expires the current keys during a sustained outage). Done as
// a second, DI-aware configuration so the custom retriever can log fetch/parse failures through
// ILoggerFactory. No-op under E2E_TESTING (that path resolves a local HS256 key and must never hit
// the network) and when no JwksUrl is configured.
builder.Services
	.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
	.Configure<ILoggerFactory>((options, loggerFactory) =>
	{
		var isE2ETesting = string.Equals(
			Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
		SupabaseJwtBearerConfiguration.AttachConfigurationManager(
			options, builder.Configuration, loggerFactory, isE2ETesting);
	});

builder.Services
	.AddAuthorizationBuilder()
	.AddRolePolicies();

builder.Services.AddSingleton<StartupMigrationRunner>();
// E2E-only diagnostic (no-ops outside E2E_TESTING) — see ThreadPoolDiagnosticsService's remarks.
builder.Services.AddHostedService<ThreadPoolDiagnosticsService>();
builder.Services.AddHealthChecks()
	.AddCheck<StartupMigrationHealthCheck>(
		"startup-migrations",
		failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
		tags: ["ready", "critical"]);

var app = builder.Build();

var migrationRunner = app.Services.GetRequiredService<StartupMigrationRunner>();

await migrationRunner.RunAsync("companies", app.Services, async sp =>
{
	await sp.MigrateAndSeedCoreApplicationAsync();
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
	await sp.SeedPlatformAdministratorsFromConfigAsync(app.Configuration);
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

await migrationRunner.RunAsync("marketing", app.Services, async sp =>
{
	await sp.MigrateMarketingAsync();
	await sp.SeedMarketingAsync();
});

await migrationRunner.RunAsync("notifications", app.Services, async sp =>
{
	await sp.MigrateNotificationsAsync();
	await sp.SeedNotificationsAsync();
	if (string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase))
	{
		await sp.SeedE2eOperationalAlertsAsync();
	}
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

app.MapGet("/health/startup-migrations", (HttpContext httpContext) => migrationRunner.ToHealthResult(httpContext));

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

if (app.Environment.IsDevelopment() && devToolsOptions.Enabled)
{
	app.MapGet("/api/dev/personas", (DevPersonaStore store) => store.AllPersonas.ToList()).AllowAnonymous();
	app.MapPost("/api/dev/persona/{userId}", async (string userId, DevPersonaStore store, IServiceProvider services) =>
	{
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

	app.MapDevLocalStorageDelivery();
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
app.UseTasksRecurringJobs();
app.UseMarketingRecurringJobs();
app.UseCompaniesRecurringJobs();
app.UseCompanyOnboardingRecurringJobs();
app.UseDataImportRecurringJobs();
app.UseLoggingMiddleware();

// Ticket 1 Phase 3: middleware to catch CustomerDatabaseUnavailableException and return 503.
app.Use(async (context, next) =>
{
	try
	{
		await next();
	}
	catch (HR.SharedKernel.CustomerDatabaseUnavailableException)
	{
		context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
		context.Response.ContentType = "application/json";
		var response = new { error = "The assigned database is temporarily unavailable. Please try again later.", code = "database_unavailable" };
		await context.Response.WriteAsJsonAsync(response);
	}
});

app.UseForwardedHeaders();
if (app.Environment.IsDevelopment() && devToolsOptions.Enabled)
{
	app.UseLoopbackOnlyForDevTools();
}
app.UseRouting();
app.Use(IdentityRateLimiting.ExtractSecondaryRateLimitKeyAsync);
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
		logger.LogError(ex, "Contact form submission failed: {ErrorType}", ex.GetType().Name);
		return Results.Problem("We couldn't send your message. Please try again shortly.", statusCode: StatusCodes.Status502BadGateway);
	}

	logger.LogInformation("Contact form submission succeeded");
	return Results.Ok();
}).AllowAnonymous().RequireRateLimiting(RateLimitRejectionLogging.ContactFormPolicy);

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

	// Ticket 3 (P1) follow-up item 4: validate the "Idempotency-Key" header, if present, before any
	// endpoint's own handler runs.
	c.Endpoints.Configurator = ep => ep.PreProcessors(Order.Before, new HR.SharedKernel.Idempotency.IdempotencyKeyHeaderValidator());
});
app.MapDefaultEndpoints();

app.Run();

public partial class Program;

internal sealed record RegisterDevPersonaRequest(Guid UserId, Guid CompanyId, string FirstName, string LastName, string Email);

internal sealed record ContactRequest(
	string Name,
	string Email,
	string Company,
	int? EmployeeCount,
	string Message,
	string? Website);
