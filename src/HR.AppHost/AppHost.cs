var builder = DistributedApplication.CreateBuilder(args);

var isE2ETesting = string.Equals(
	Environment.GetEnvironmentVariable("E2E_TESTING"),
	"true",
	StringComparison.OrdinalIgnoreCase);

// Pinned to the standard Postgres port rather than Aspire's dynamic port allocation — on this
// machine, Windows/Hyper-V reserves large chunks of the ephemeral port range (see
// `netsh interface ipv4 show excludedportrange protocol=tcp`), and Aspire's dynamic picker
// occasionally lands in one of those excluded ranges, causing "Unable to allocate a network
// port for service 'postgres'" and leaving the whole app without a working DB connection.
var postgres = builder.AddPostgres("postgres").WithHostPort(5432);
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

// Public marketing site — static content only, no database/API dependency.
var marketing = isE2ETesting
	? builder.AddProject<Projects.HR_Marketing>("marketing", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Marketing>("marketing");

builder.Build().Run();
