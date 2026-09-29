var builder = DistributedApplication.CreateBuilder(args);

var isE2ETesting = string.Equals(
	Environment.GetEnvironmentVariable("E2E_TESTING"),
	"true",
	StringComparison.OrdinalIgnoreCase);

// Ticket 1 — block production test authentication. E2E_TESTING enables fake auth and test doubles
// across the orchestrated services; it must never be honoured outside local/CI E2E runs, which
// execute as Development. Fail fast rather than silently launch a test-auth-enabled topology.
if (isE2ETesting && !string.Equals(builder.Environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
{
	throw new InvalidOperationException(
		$"E2E_TESTING=true is not permitted in the '{builder.Environment.EnvironmentName}' environment. "
		+ "Test authentication is only allowed under Development. Refusing to start.");
}

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
	postgres = postgres.WithArgs("-c", "max_connections=500");
}

var hrDatabase = postgres.AddDatabase("hr");

var clamAv = builder.AddContainer("clamav", "clamav/clamav", "stable")
	.WithEndpoint(port: 3310, targetPort: 3310, name: "clamd");

var api = isE2ETesting
	? builder.AddProject<Projects.HR_Api>("api", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Api>("api");

api
    .WithReference(hrDatabase)
    .WaitFor(hrDatabase)
    .WithEnvironment("Documents__ClamAv__Host", clamAv.GetEndpoint("clamd").Property(EndpointProperty.Host))
    .WithEnvironment("Documents__ClamAv__Port", clamAv.GetEndpoint("clamd").Property(EndpointProperty.Port))
    .WaitFor(clamAv);

if (isE2ETesting)
{
	api.WithEnvironment("Identity__RateLimits__identity-login__PermitLimit", "100");
	api.WithEnvironment("Identity__RateLimits__identity-signup__PermitLimit", "100");
}

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

var marketing = isE2ETesting
	? builder.AddProject<Projects.HR_Marketing>("marketing", launchProfileName: "http")
	: builder.AddProject<Projects.HR_Marketing>("marketing");

marketing
	.WithReference(web)
	.WithReference(api)
	.WaitFor(api);

web.WithReference(marketing);

builder.Build().Run();
