using Hangfire;
using HR.Modules.Companies.Services;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HR.Integration.Tests.Infrastructure;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("hr_integration")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public FakeEmailSender EmailSender { get; } = new FakeEmailSender();

    public FakeInvitationEmailSender InvitationEmailSender { get; private set; } = null!;

    internal FakeStripeGateway StripeGateway { get; } = new FakeStripeGateway();

    internal FakeSupabaseAuthGateway SupabaseAuthGateway { get; } = new FakeSupabaseAuthGateway();

    // Ticket 3 (P1) final gap item 5: shared across every test in the collection, like the other
    // fakes above — each test arms/resets it around its own request(s) rather than getting its own
    // factory instance.
    internal FaultInjectingPostCommitFaultInjector PostCommitFaultInjector { get; } = new();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__hr", _postgres.GetConnectionString());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__hr", null);
        await _postgres.DisposeAsync();
    }

    // A fixed, throwaway AES-256 key (32 zero bytes, base64) so the sensitive-data protector is
    // resolvable in integration tests. Required for any test that persists an application-encrypted
    // column (e.g. employee equality-monitoring answers) and asserts on ciphertext at rest.
    private const string TestSensitiveDataKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:SensitiveDataProtection:ActiveKeyId"] = "test",
                ["Infrastructure:SensitiveDataProtection:Keys:test"] = TestSensitiveDataKey,
                // P1 "Login as Customer": a fixed, throwaway key so ISupportSessionTokenIssuer /
                // SupportSessionJwtBearerConfiguration are resolvable in integration tests — never
                // a real secret, mirrors TestSensitiveDataKey's own convention above.
                ["SupportSession:SigningKey"] = "test-support-session-signing-key-not-a-real-secret",
                // P1 identity rate limiting: this shared factory's tests call Login/SignUp/
                // forgot-password/etc. far more often per class run than the production defaults
                // allow (many cases exercising the SAME email/IP combination in one run). Widened
                // here — mirrors the identical convention already used for the marketing
                // contact-form limiter (Marketing:ContactForm:RateLimit:*) — rather than disabling
                // the policies outright, so the policies themselves are still genuinely exercised.
                ["Identity:RateLimits:identity-login:PermitLimit"] = "1000",
                ["Identity:RateLimits:identity-signup:PermitLimit"] = "1000",
                ["Identity:RateLimits:identity-forgot-password:PermitLimit"] = "1000",
                ["Identity:RateLimits:identity-resend-verification:PermitLimit"] = "1000",
                ["Identity:RateLimits:identity-accept-invite:PermitLimit"] = "1000",
                ["Identity:RateLimits:identity-reset-password:PermitLimit"] = "1000",
                // Ticket 1: Customer database assignments — test configuration for the resolver's
                // database_key lookup path. "cust-test-db1" is a valid configured key; other keys
                // are intentionally not configured to test the missing-config exception path.
                ["CustomerDatabases:cust-test-db1:ConnectionString"] = "Host=localhost;Port=5432;Database=cust_test_db1;Username=postgres;Password=postgres",
            });
        });

        builder.ConfigureServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ =>
                    {
                    });

            // Replace real email sender and link builder with test doubles
            services.AddSingleton<IEmailSender>(EmailSender);
            services.AddSingleton<IInviteLinkBuilder, FakeInviteLinkBuilder>();

            // The invitation path uses the branded-template IInvitationEmailSender rather than the
            // raw IEmailSender — capture those sends into the same FakeEmailSender.Sent surface.
            InvitationEmailSender = new FakeInvitationEmailSender(EmailSender);
            services.AddSingleton<IInvitationEmailSender>(InvitationEmailSender);

            // Replace the real Stripe gateway so no test ever calls out to Stripe's network API.
            services.AddScoped<IStripeGateway>(_ => StripeGateway);

            // Replace the real Supabase Auth gateway so no test ever calls out to Supabase's live
            // Auth Admin API.
            services.AddScoped<ISupabaseAuthGateway>(_ => SupabaseAuthGateway);

            // Replace the real Hangfire-backed IBackgroundJobClient with a no-op fake. Registered
            // after AddHangfireBackgroundJobs (Program.cs) has already wired up the real Hangfire
            // server/storage against the Postgres testcontainer, so this override wins for the
            // IBackgroundJobClient interface while leaving the Hangfire server/dashboard/health
            // check plumbing itself intact. See FakeBackgroundJobClient for why this matters: real
            // job execution otherwise races test-driven state (e.g. ScanUploadedFileJob vs a
            // test's manual "mark scan clean" step).
            services.AddSingleton<IBackgroundJobClient, FakeBackgroundJobClient>();

            // Ticket 3 (P1) final gap item 5: replace the production no-op with the shared,
            // test-armable double so AdjustLeaveBalanceIdempotencyFaultTests /
            // CreateAssetIdempotencyFaultTests can simulate a post-commit (or pre-commit) failure
            // for a specific handler + idempotency key without any production code depending on
            // test infrastructure.
            services.AddSingleton<HR.SharedKernel.Idempotency.IPostCommitFaultInjector>(PostCommitFaultInjector);
        });
    }
}
