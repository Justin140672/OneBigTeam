using FastEndpoints;
using HR.Api.Authentication;
using HR.Infrastructure;
using HR.Infrastructure.Logging;
using HR.Modules.Companies;
using HR.Modules.DataImport;
using HR.Modules.Documents;
using HR.Modules.Employees;
using HR.Modules.Identity;
using HR.Modules.Leave;
using HR.Modules.Notifications;
using HR.Modules.Onboarding;
using HR.Modules.Assets;
using HR.Modules.Sickness;
using HR.Modules.Probation;
using HR.Modules.Recruitment;
using HR.Modules.Tasks;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Host.UseSerilogWithDefaults();

var connectionString = builder.Configuration.GetConnectionString("hr")
	?? throw new InvalidOperationException("Connection string 'hr' was not found.");

builder.Services.AddCompaniesModule(connectionString);
builder.Services.AddDataImportModule(connectionString, builder.Configuration);
builder.Services.AddDocumentsModule(connectionString, builder.Configuration);
builder.Services.AddEmployeesModule(connectionString);
builder.Services.AddIdentityModule(connectionString);
builder.Services.AddLeaveModule(connectionString);
builder.Services.AddNotificationsModule(connectionString);
builder.Services.AddOnboardingModule(connectionString);
builder.Services.AddTasksModule(connectionString);
builder.Services.AddProbationModule(connectionString);
builder.Services.AddRecruitmentModule(connectionString, builder.Configuration);
builder.Services.AddAssetsModule(connectionString);
builder.Services.AddSicknessModule(connectionString);
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHangfireBackgroundJobs(connectionString);
builder.Services.AddFastEndpoints(o => o.IncludeAbstractValidators = true);
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

if (builder.Environment.IsDevelopment())
{
	builder.Services.AddSingleton<DevPersonaStore>();
	builder.Services
		.AddAuthentication("DevAuth")
		.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthHandler>(
			"DevAuth", _ => { });
}
else
{
	builder.Services
		.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
		.AddJwtBearer();
}

builder.Services
	.AddAuthorizationBuilder()
	.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser())
	.AddRolePolicies();

var app = builder.Build();

var companiesMigrationStatus = "unknown";
string? companiesMigrationError = null;
DateTimeOffset? companiesMigrationCheckedAt = null;
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
var probationMigrationStatus = "unknown";
string? probationMigrationError = null;
DateTimeOffset? probationMigrationCheckedAt = null;
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
		probation = new
		{
			status = probationMigrationStatus,
			checkedAt = probationMigrationCheckedAt,
			error = probationMigrationError
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

	return auditMigrationStatus == "failed" || companiesMigrationStatus == "failed" || dataImportMigrationStatus == "failed" || documentsMigrationStatus == "failed" || employeesMigrationStatus == "failed" || identityMigrationStatus == "failed" || leaveMigrationStatus == "failed" || notificationsMigrationStatus == "failed" || tasksMigrationStatus == "failed" || onboardingMigrationStatus == "failed" || probationMigrationStatus == "failed" || assetsMigrationStatus == "failed" || sicknessMigrationStatus == "failed" || recruitmentMigrationStatus == "failed"
		? Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable)
		: Results.Ok(response);
});
if (app.Environment.IsDevelopment())
{
	app.MapGet("/api/dev/personas", (DevPersonaStore store) => DevPersonaStore.Personas).AllowAnonymous();
	app.MapPost("/api/dev/persona/{userId}", (string userId, DevPersonaStore store) =>
	{
		store.Switch(userId);
		return Results.NoContent();
	}).AllowAnonymous();
}

app.UseHangfireBackgroundJobs();
app.UseProbationRecurringJobs();
app.UseAssetsRecurringJobs();
app.UseSicknessRecurringJobs();
app.UseRecruitmentRecurringJobs();
app.UseOnboardingRecurringJobs();
app.UseLoggingMiddleware();
app.UseRouting();
app.UseAuthentication();
app.UseIdentityModule();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
	c.Serializer.Options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
	c.Errors.StatusCode = 422;
});
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
