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

    internal FakeStripeGateway StripeGateway { get; } = new FakeStripeGateway();

    internal FakeSupabaseAuthGateway SupabaseAuthGateway { get; } = new FakeSupabaseAuthGateway();

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
                ["Infrastructure:SensitiveDataProtection:Keys:test"] = TestSensitiveDataKey
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
            services.AddSingleton<IInvitationEmailSender>(new FakeInvitationEmailSender(EmailSender));

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
        });
    }
}
