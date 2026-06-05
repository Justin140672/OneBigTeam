using HR.Modules.Companies;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("hr")
	?? throw new InvalidOperationException("Connection string 'hr' was not found.");

builder.Services.AddCompaniesModule(connectionString);

var app = builder.Build();

await app.Services.MigrateCompaniesAsync();

app.MapGet("/", () => "Hello World!");
app.MapDefaultEndpoints();

app.Run();
