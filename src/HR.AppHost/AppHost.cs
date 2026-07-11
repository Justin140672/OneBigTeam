var builder = DistributedApplication.CreateBuilder(args);

var isE2ETesting = string.Equals(
	Environment.GetEnvironmentVariable("E2E_TESTING"),
	"true",
	StringComparison.OrdinalIgnoreCase);

var postgres = builder.AddPostgres("postgres");
var hrDatabase = postgres.AddDatabase("hr");

var api = isE2ETesting
	? builder.AddProject<Projects.HR_Api>("api", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Api>("api");

api
    .WithReference(hrDatabase)
    .WaitFor(hrDatabase);

var web = isE2ETesting
	? builder.AddProject<Projects.HR_Web>("web", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Web>("web");

web
	.WithReference(api)
	.WaitFor(api);

builder.Build().Run();
