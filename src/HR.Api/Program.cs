using FastEndpoints;
using HR.Modules.Companies;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("hr")
	?? throw new InvalidOperationException("Connection string 'hr' was not found.");

builder.Services.AddCompaniesModule(connectionString);
builder.Services.AddFastEndpoints();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services
	.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer();
builder.Services
	.AddAuthorizationBuilder()
	.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

var app = builder.Build();

var companiesMigrationStatus = "unknown";
string? companiesMigrationError = null;
DateTimeOffset? companiesMigrationCheckedAt = null;

try
{
	await app.Services.MigrateCompaniesAsync();
	companiesMigrationStatus = "succeeded";
	companiesMigrationCheckedAt = DateTimeOffset.UtcNow;
}
catch (Exception exception)
{
	companiesMigrationStatus = "failed";
	companiesMigrationError = exception.Message;
	companiesMigrationCheckedAt = DateTimeOffset.UtcNow;
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
		}
	};

	return companiesMigrationStatus == "failed"
		? Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable)
		: Results.Ok(response);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
