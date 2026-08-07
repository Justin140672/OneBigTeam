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

// ClamAV daemon for virus-scanning uploaded documents/photos (Documents module,
// ScanUploadedFileJob) — exposed on its default clamd port. Local/dev environments without this
// configured fall back to NoOpVirusScanService (see DocumentsModule.AddStorageService).
var clamAv = builder.AddContainer("clamav", "clamav/clamav", "stable")
	.WithEndpoint(port: 3310, targetPort: 3310, name: "clamd");

var api = isE2ETesting
	? builder.AddProject<Projects.HR_Api>("api", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Api>("api");

api
    .WithReference(hrDatabase)
    .WaitFor(hrDatabase)
    // clamd speaks raw TCP (INSTREAM protocol), not HTTP, so Aspire's default HTTP-based service
    // discovery URL format doesn't apply here — bind the container's "clamd" endpoint host/port
    // directly onto the config keys ClamAvOptions/DocumentsModule.AddStorageService bind to
    // ("Documents:ClamAv:Host" / ":Port"), so the real ClamAvVirusScanService is registered
    // instead of silently falling back to NoOpVirusScanService.
    .WithEnvironment("Documents__ClamAv__Host", clamAv.GetEndpoint("clamd").Property(EndpointProperty.Host))
    .WithEnvironment("Documents__ClamAv__Port", clamAv.GetEndpoint("clamd").Property(EndpointProperty.Port))
    .WaitFor(clamAv);

var web = isE2ETesting
	? builder.AddProject<Projects.HR_Web>("web", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Web>("web");

web
	.WithReference(api)
	.WaitFor(api);

// Public marketing site — static content, plus a server-side "Start free trial" signup proxy
// (Phase B of the Getting Started + Subscription/Billing epic) that calls HR.Api's public /api/signup
// endpoint directly, avoiding any need for browser-side CORS. References web to resolve its URL for
// the "Log in" link and the post-signup redirect into "/getting-started".
var marketing = isE2ETesting
	? builder.AddProject<Projects.HR_Marketing>("marketing", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Marketing>("marketing");

marketing
	.WithReference(web)
	.WithReference(api)
	.WaitFor(api);

builder.Build().Run();
