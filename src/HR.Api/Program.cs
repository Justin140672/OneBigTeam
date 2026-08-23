using System.Net;
using System.Threading.RateLimiting;
using FastEndpoints;
using HR.Api.Authentication;
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
connectionString += ";Maximum Pool Size=300;Minimum Pool Size=10";

builder.Services.AddCompaniesModule(connectionString, builder.Configuration);
builder.Services.AddCompanyOnboardingModule(connectionString);
builder.Services.AddDataImportModule(connectionString, builder.Configuration);
builder.Services.AddDocumentsModule(connectionString, builder.Configuration);
builder.Services.AddEmployeesModule(connectionString);
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

var app = builder.Build();

var companiesMigrationStatus = "unknown";
string? companiesMigrationError = null;
DateTimeOffset? companiesMigrationCheckedAt = null;
var companyOnboardingMigrationStatus = "unknown";
string? companyOnboardingMigrationError = null;
DateTimeOffset? companyOnboardingMigrationCheckedAt = null;
var dataImportMigrationStatus = "unknown";
string? dataImportMigrationError = null;
DateTimeOffset? dataImportMigrationCheckedAt = null;
var employeesMigrationStatus = "unknown";
string? employeesMigrationError = null;
DateTimeOffset? employeesMigrationCheckedAt = null;
var identityMigrationStatus = "unknown";
string? identityMigrationError = null;
DateTimeOffset? identityMigrationCheckedAt = null;
var auditMigrationStatus = "unknown";
string? auditMigrationError = null;
DateTimeOffset? auditMigrationCheckedAt = null;
var documentsMigrationStatus = "unknown";
string? documentsMigrationError = null;
DateTimeOffset? documentsMigrationCheckedAt = null;
var leaveMigrationStatus = "unknown";
string? leaveMigrationError = null;
DateTimeOffset? leaveMigrationCheckedAt = null;
var tasksMigrationStatus = "unknown";
string? tasksMigrationError = null;
DateTimeOffset? tasksMigrationCheckedAt = null;
var notificationsMigrationStatus = "unknown";
string? notificationsMigrationError = null;
DateTimeOffset? notificationsMigrationCheckedAt = null;
var onboardingMigrationStatus = "unknown";
string? onboardingMigrationError = null;
DateTimeOffset? onboardingMigrationCheckedAt = null;
var offboardingMigrationStatus = "unknown";
string? offboardingMigrationError = null;
DateTimeOffset? offboardingMigrationCheckedAt = null;
var probationMigrationStatus = "unknown";
string? probationMigrationError = null;
DateTimeOffset? probationMigrationCheckedAt = null;
var reportingMigrationStatus = "unknown";
string? reportingMigrationError = null;
DateTimeOffset? reportingMigrationCheckedAt = null;
var assetsMigrationStatus = "unknown";
string? assetsMigrationError = null;
DateTimeOffset? assetsMigrationCheckedAt = null;

var supportMigrationStatus = "unknown";
string? supportMigrationError = null;
DateTimeOffset? supportMigrationCheckedAt = null;
var sicknessMigrationStatus = "unknown";
string? sicknessMigrationError = null;
DateTimeOffset? sicknessMigrationCheckedAt = null;
var recruitmentMigrationStatus = "unknown";
string? recruitmentMigrationError = null;
DateTimeOffset? recruitmentMigrationCheckedAt = null;

try
{
	await app.Services.MigrateCompaniesAsync();
	await app.Services.SeedCompaniesAsync();
	companiesMigrationStatus = "succeeded";
	companiesMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	companiesMigrationStatus = "failed";
	companiesMigrationError = exception.Message;
	companiesMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateCompanyOnboardingAsync();
	await app.Services.SeedCompanyOnboardingAsync();
	companyOnboardingMigrationStatus = "succeeded";
	companyOnboardingMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	companyOnboardingMigrationStatus = "failed";
	companyOnboardingMigrationError = exception.Message;
	companyOnboardingMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateDataImportAsync();
	dataImportMigrationStatus = "succeeded";
	dataImportMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	dataImportMigrationStatus = "failed";
	dataImportMigrationError = exception.Message;
	dataImportMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateEmployeesAsync();
	await app.Services.SeedEmployeesAsync();
	employeesMigrationStatus = "succeeded";
	employeesMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	employeesMigrationStatus = "failed";
	employeesMigrationError = exception.Message;
	employeesMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateIdentityAsync();
	// Bootstrap PlatformAdministrator rows from the PlatformAdmin:AllowedEmails config allow-list
	// (see IdentityModule.SeedPlatformAdministratorsFromConfigAsync remarks). Runs in every
	// environment, not just Development, since the allow-list itself is configured per-environment
	// (appsettings.json / appsettings.Staging.json / production config) and admin accounts must
	// exist as real PlatformAdministrator rows wherever the Admin Portal is reachable. Idempotent.
	await app.Services.SeedPlatformAdministratorsFromConfigAsync(app.Configuration);
	if (app.Environment.IsDevelopment())
	{
		await app.Services.SeedDevUserAsync();
		await app.Services.SeedDevSupabaseUsersAsync(DevPersonaStore.Personas.Select(p =>
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
	identityMigrationStatus = "succeeded";
	identityMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	identityMigrationStatus = "failed";
	identityMigrationError = exception.Message;
	identityMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateAuditAsync();
	auditMigrationStatus = "succeeded";
	auditMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	auditMigrationStatus = "failed";
	auditMigrationError = exception.Message;
	auditMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateDocumentsAsync();
	await app.Services.SeedDocumentsAsync();
	documentsMigrationStatus = "succeeded";
	documentsMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	documentsMigrationStatus = "failed";
	documentsMigrationError = exception.Message;
	documentsMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateLeaveAsync();
	await app.Services.SeedLeaveAsync();
	leaveMigrationStatus = "succeeded";
	leaveMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	leaveMigrationStatus = "failed";
	leaveMigrationError = exception.Message;
	leaveMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateNotificationsAsync();
	await app.Services.SeedNotificationsAsync();
	notificationsMigrationStatus = "succeeded";
	notificationsMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	notificationsMigrationStatus = "failed";
	notificationsMigrationError = exception.Message;
	notificationsMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateTasksAsync();
	await app.Services.SeedTasksAsync();
	tasksMigrationStatus = "succeeded";
	tasksMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	tasksMigrationStatus = "failed";
	tasksMigrationError = exception.Message;
	tasksMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateOnboardingAsync();
	onboardingMigrationStatus = "succeeded";
	onboardingMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	onboardingMigrationStatus = "failed";
	onboardingMigrationError = exception.Message;
	onboardingMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateOffboardingAsync();
	offboardingMigrationStatus = "succeeded";
	offboardingMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	offboardingMigrationStatus = "failed";
	offboardingMigrationError = exception.Message;
	offboardingMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateProbationAsync();
	await app.Services.SeedProbationAsync();
	probationMigrationStatus = "succeeded";
	probationMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	probationMigrationStatus = "failed";
	probationMigrationError = exception.Message;
	probationMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateReportingAsync();
	reportingMigrationStatus = "succeeded";
	reportingMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	reportingMigrationStatus = "failed";
	reportingMigrationError = exception.Message;
	reportingMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateAssetsAsync();
	await app.Services.SeedAssetsAsync();
	assetsMigrationStatus = "succeeded";
	assetsMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	assetsMigrationStatus = "failed";
	assetsMigrationError = exception.Message;
	assetsMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateSicknessAsync();
	await app.Services.SeedSicknessAsync();
	sicknessMigrationStatus = "succeeded";
	sicknessMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	sicknessMigrationStatus = "failed";
	sicknessMigrationError = exception.Message;
	sicknessMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateSupportAsync();
	await app.Services.SeedSupportAsync();
	supportMigrationStatus = "succeeded";
	supportMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	supportMigrationStatus = "failed";
	supportMigrationError = exception.Message;
	supportMigrationCheckedAt = DateTimeOffset.UtcNow;
}

try
{
	await app.Services.MigrateRecruitmentAsync();
	await app.Services.SeedRecruitmentAsync();
	recruitmentMigrationStatus = "succeeded";
	recruitmentMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	recruitmentMigrationStatus = "failed";
	recruitmentMigrationError = exception.Message;
	recruitmentMigrationCheckedAt = DateTimeOffset.UtcNow;
}

app.MapGet("/health/startup-migrations", () =>
{
	var response = new
	{
		audit = new
		{
			status = auditMigrationStatus,
			checkedAt = auditMigrationCheckedAt,
			error = auditMigrationError
		},
		companies = new
		{
			status = companiesMigrationStatus,
			checkedAt = companiesMigrationCheckedAt,
			error = companiesMigrationError
		},
		companyOnboarding = new
		{
			status = companyOnboardingMigrationStatus,
			checkedAt = companyOnboardingMigrationCheckedAt,
			error = companyOnboardingMigrationError
		},
		dataImport = new
		{
			status = dataImportMigrationStatus,
			checkedAt = dataImportMigrationCheckedAt,
			error = dataImportMigrationError
		},
		documents = new
		{
			status = documentsMigrationStatus,
			checkedAt = documentsMigrationCheckedAt,
			error = documentsMigrationError
		},
		employees = new
		{
			status = employeesMigrationStatus,
			checkedAt = employeesMigrationCheckedAt,
			error = employeesMigrationError
		},
		identity = new
		{
			status = identityMigrationStatus,
			checkedAt = identityMigrationCheckedAt,
			error = identityMigrationError
		},
		leave = new
		{
			status = leaveMigrationStatus,
			checkedAt = leaveMigrationCheckedAt,
			error = leaveMigrationError
		},
		notifications = new
		{
			status = notificationsMigrationStatus,
			checkedAt = notificationsMigrationCheckedAt,
			error = notificationsMigrationError
		},
		tasks = new
		{
			status = tasksMigrationStatus,
			checkedAt = tasksMigrationCheckedAt,
			error = tasksMigrationError
		},
		onboarding = new
		{
			status = onboardingMigrationStatus,
			checkedAt = onboardingMigrationCheckedAt,
			error = onboardingMigrationError
		},
		offboarding = new
		{
			status = offboardingMigrationStatus,
			checkedAt = offboardingMigrationCheckedAt,
			error = offboardingMigrationError
		},
		probation = new
		{
			status = probationMigrationStatus,
			checkedAt = probationMigrationCheckedAt,
			error = probationMigrationError
		},
		reporting = new
		{
			status = reportingMigrationStatus,
			checkedAt = reportingMigrationCheckedAt,
			error = reportingMigrationError
		},
		assets = new
		{
			status = assetsMigrationStatus,
			checkedAt = assetsMigrationCheckedAt,
			error = assetsMigrationError
		},
		sickness = new
		{
			status = sicknessMigrationStatus,
			checkedAt = sicknessMigrationCheckedAt,
			error = sicknessMigrationError
		},
		recruitment = new
		{
			status = recruitmentMigrationStatus,
			checkedAt = recruitmentMigrationCheckedAt,
			error = recruitmentMigrationError
		},
		support = new
		{
			status = supportMigrationStatus,
			checkedAt = supportMigrationCheckedAt,
			error = supportMigrationError
		}
	};

	return auditMigrationStatus == "failed" || companiesMigrationStatus == "failed" || companyOnboardingMigrationStatus == "failed" || dataImportMigrationStatus == "failed" || documentsMigrationStatus == "failed" || employeesMigrationStatus == "failed" || identityMigrationStatus == "failed" || leaveMigrationStatus == "failed" || notificationsMigrationStatus == "failed" || tasksMigrationStatus == "failed" || onboardingMigrationStatus == "failed" || offboardingMigrationStatus == "failed" || probationMigrationStatus == "failed" || reportingMigrationStatus == "failed" || assetsMigrationStatus == "failed" || sicknessMigrationStatus == "failed" || recruitmentMigrationStatus == "failed" || supportMigrationStatus == "failed"
		? Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable)
		: Results.Ok(response);
});
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
app.UseProbationRecurringJobs();
app.UseAssetsRecurringJobs();
app.UseSicknessRecurringJobs();
app.UseSupportRecurringJobs();
app.UseRecruitmentRecurringJobs();
app.UseOnboardingRecurringJobs();
app.UseOffboardingRecurringJobs();
app.UseDocumentsRecurringJobs();
app.UseLeaveRecurringJobs();
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
