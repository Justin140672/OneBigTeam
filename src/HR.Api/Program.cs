using FastEndpoints;
using HR.Api.Authentication;
using HR.Modules.Companies;
using HR.Modules.Employees;
using HR.Modules.Identity;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("hr")
	?? throw new InvalidOperationException("Connection string 'hr' was not found.");

builder.Services.AddCompaniesModule(connectionString);
builder.Services.AddEmployeesModule(connectionString);
builder.Services.AddIdentityModule(connectionString);
builder.Services.AddFastEndpoints();
builder.Services.AddSingleton<IClock, SystemClock>();

if (builder.Environment.IsDevelopment())
{
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
	.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

var app = builder.Build();

var companiesMigrationStatus = "unknown";
string? companiesMigrationError = null;
DateTimeOffset? companiesMigrationCheckedAt = null;
var employeesMigrationStatus = "unknown";
string? employeesMigrationError = null;
DateTimeOffset? employeesMigrationCheckedAt = null;
var identityMigrationStatus = "unknown";
string? identityMigrationError = null;
DateTimeOffset? identityMigrationCheckedAt = null;

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
	identityMigrationStatus = "succeeded";
	identityMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	identityMigrationStatus = "failed";
	identityMigrationError = exception.Message;
	identityMigrationCheckedAt = DateTimeOffset.UtcNow;
}

app.MapGet("/health/startup-migrations", () =>
{
	var response = new
	{
		companies = new
		{
			status = companiesMigrationStatus,
			checkedAt = companiesMigrationCheckedAt,
			error = companiesMigrationError
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
		}
	};

	return companiesMigrationStatus == "failed" || employeesMigrationStatus == "failed" || identityMigrationStatus == "failed"
		? Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable)
		: Results.Ok(response);
});
app.UseAuthentication();
app.UseIdentityModule();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
	c.Serializer.Options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
