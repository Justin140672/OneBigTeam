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
using HR.Modules.Tasks;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Host.UseSerilogWithDefaults();

var connectionString = builder.Configuration.GetConnectionString("hr")
	?? throw new InvalidOperationException("Connection string 'hr' was not found.");

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
builder.Services.AddReportingModule(connectionString);
builder.Services.AddInfrastructure(connectionString, builder.Configuration);
builder.Services.AddHangfireBackgroundJobs(connectionString);
builder.Services.AddFastEndpoints(o => o.IncludeAbstractValidators = true);
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

// Supabase-backed JWT Bearer validation, shared between Development (dual-scheme below) and
// non-Development (sole scheme). This Supabase project uses asymmetric JWT signing (JWKS), not a
// shared HS256 secret — see SupabaseJwksKeyResolver below.
void ConfigureSupabaseJwtBearer(JwtBearerOptions options)
{
	var supabaseProjectUrl = builder.Configuration["SupabaseAuth:ProjectUrl"] ?? "";
	var supabaseJwksUrl = builder.Configuration["SupabaseAuth:JwksUrl"] ?? "";

	options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
	{
		ValidateIssuer = true,
		ValidIssuer = $"{supabaseProjectUrl}/auth/v1",
		ValidateAudience = true,
		// "authenticated" is Supabase's well-known audience claim value for authenticated users on
		// issued access tokens.
		ValidAudience = "authenticated",
		ValidateLifetime = true,
		IssuerSigningKeyResolver = (_, _, kid, _) =>
			SupabaseJwksKeyResolver.ResolveSigningKeys(supabaseJwksUrl, kid),
	};
}

if (builder.Environment.IsDevelopment())
{
	builder.Services.AddSingleton<DevPersonaStore>();

	// Dual-scheme: requests carrying a real Authorization: Bearer header (e.g. HR.Web's
	// /verify-email flow calling back in with a genuine Supabase access token, or anyone testing
	// real Supabase Auth end-to-end locally) are validated as real Supabase JWTs; everything else
	// keeps using the dev-persona auto-login as before. Without this, Development could never
	// authenticate a real Supabase-issued token at all — DevAuthHandler ignores the header
	// entirely and always signs the request in as the fixed dev persona.
	builder.Services
		.AddAuthentication("DevOrSupabase")
		.AddPolicyScheme("DevOrSupabase", "DevOrSupabase", options =>
		{
			options.ForwardDefaultSelector = context =>
				context.Request.Headers.ContainsKey("Authorization")
					? JwtBearerDefaults.AuthenticationScheme
					: "DevAuth";
		})
		.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthHandler>(
			"DevAuth", _ => { })
		.AddJwtBearer(ConfigureSupabaseJwtBearer);
}
else
{
	// Supabase-backed authentication for non-Development environments (Phase B of the "Move Email
	// Verification into the Main Application" plan).
	builder.Services
		.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
		.AddJwtBearer(ConfigureSupabaseJwtBearer);
}

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
	if (app.Environment.IsDevelopment())
		await app.Services.SeedDevUserAsync();
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
		}
	};

	return auditMigrationStatus == "failed" || companiesMigrationStatus == "failed" || companyOnboardingMigrationStatus == "failed" || dataImportMigrationStatus == "failed" || documentsMigrationStatus == "failed" || employeesMigrationStatus == "failed" || identityMigrationStatus == "failed" || leaveMigrationStatus == "failed" || notificationsMigrationStatus == "failed" || tasksMigrationStatus == "failed" || onboardingMigrationStatus == "failed" || offboardingMigrationStatus == "failed" || probationMigrationStatus == "failed" || reportingMigrationStatus == "failed" || assetsMigrationStatus == "failed" || sicknessMigrationStatus == "failed" || recruitmentMigrationStatus == "failed"
		? Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable)
		: Results.Ok(response);
});
if (app.Environment.IsDevelopment())
{
	app.MapGet("/api/dev/personas", (DevPersonaStore store) => DevPersonaStore.Personas).AllowAnonymous();
	app.MapPost("/api/dev/persona/{userId}", async (string userId, DevPersonaStore store, IServiceProvider services) =>
	{
		// The dev persona switcher is the only real "sign-in" path in this codebase today (see
		// HR.Modules.Identity.IdentityModule.TryDevSignInAsync remarks) — this is where the
		// IsActive gate (ticket #88) and LastLoginAt recording (ticket #89) are wired in.
		if (!Guid.TryParse(userId, out var userGuid))
			return Results.NoContent();

		var isAllowed = await services.TryDevSignInAsync(userGuid);
		if (!isAllowed)
			return Results.StatusCode(StatusCodes.Status403Forbidden);

		store.Switch(userId);
		return Results.NoContent();
	}).AllowAnonymous();

	// Establishes a dev-stub session for a brand-new self-service signup admin (HR.Modules.Identity's
	// SignUp feature returns exactly these fields). Identity cannot reference DevPersonaStore itself
	// (it lives in HR.Api, the host) — the client (marketing StartTrial page / HR.Web) calls this
	// immediately after a successful signup, achieving the "auto-login" UX via the same dev-stub
	// mechanism used everywhere else in this codebase today.
	app.MapPost("/api/dev/persona/register", (RegisterDevPersonaRequest request, DevPersonaStore store) =>
	{
		store.Register(new DevPersona(
			request.UserId.ToString(),
			request.CompanyId.ToString(),
			$"{request.FirstName} {request.LastName}".Trim(),
			"Company Administrator",
			request.Email));
		return Results.NoContent();
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
app.UseRecruitmentRecurringJobs();
app.UseOnboardingRecurringJobs();
app.UseOffboardingRecurringJobs();
app.UseDocumentsRecurringJobs();
app.UseLoggingMiddleware();
app.UseRouting();
app.UseAuthentication();
app.UseIdentityModule();
app.UseAuthorization();
app.UseCompaniesModule();
app.UseFastEndpoints(c =>
{
	c.Serializer.Options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
	c.Errors.StatusCode = 422;
});
app.MapDefaultEndpoints();

app.Run();

public partial class Program;

internal sealed record RegisterDevPersonaRequest(Guid UserId, Guid CompanyId, string FirstName, string LastName, string Email);

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
