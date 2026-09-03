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

// Persist local dev data across AppHost restarts — without this, every restart recreates an
// empty Postgres container while externally-hosted Supabase Auth users survive untouched,
// leaving real Supabase accounts with no matching local UserProfile/Company rows (confirmed via
// live diagnosis: a signed-up admin could log in once, then get "invalid email or password" and
// a null UserProfile lookup after a restart, purely because the local copy was wiped out from
// under a still-alive Supabase identity). Skipped for E2E_TESTING, which relies on each run
// starting from a clean, migrated-but-empty database.
if (!isE2ETesting)
{
	postgres = postgres.WithDataVolume();
}
else
{
	// The E2E suite runs one shared Postgres behind one shared api instance while up to 15 xUnit
	// threads drive concurrent Playwright circuits. HR.Api raises its Npgsql pool ceiling to 400
	// under E2E (see its Program.cs), and Hangfire + migrations add more on top — all of which is
	// capped by the container's own server-side limit. Postgres' stock max_connections=100 is well
	// below that, so a burst hits "sorry, too many clients already" and surfaces as the generic
	// 20s Playwright locator timeouts this infrastructure keeps fighting. Lift the server ceiling
	// clear of the client pool. (Only max_connections — raising shared_buffers would need the
	// container's /dev/shm bumped too, and the stock 128MB is fine for ~500 idle-ish sessions.)
	postgres = postgres.WithArgs("-c", "max_connections=500");
}

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

// Internal Admin Portal — platform-staff-only cross-tenant tooling (Customer Dashboard epic).
// References api directly for the "platform:admin" endpoints; deliberately does not reference web
// (no cross-app navigation needed yet).
var adminWeb = isE2ETesting
	? builder.AddProject<Projects.HR_Admin_Web>("adminweb", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Admin_Web>("adminweb");

adminWeb
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

// Reverse reference so HR.Web gets Aspire's own "services:marketing:https:0"/"...:http:0"
// service-discovery config keys injected — needed for VerifyEmailError.razor's "resend
// verification" bridge back to the marketing site's /check-your-email page, which previously had
// no way to resolve marketing's actual (dynamically-assigned) URL and fell back to a hardcoded,
// frequently-wrong "http://localhost:5166" (Marketing:BaseUrl in appsettings.Development.json).
web.WithReference(marketing);

builder.Build().Run();
