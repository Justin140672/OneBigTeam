using Hangfire;
using HR.Modules.Companies.Services;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
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

    private const string TestSensitiveDataKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:SensitiveDataProtection:ActiveKeyId"] = "test",
                ["Infrastructure:SensitiveDataProtection:Keys:test"] = TestSensitiveDataKey,
                ["SupportSession:SigningKey"] = "test-support-session-signing-key-not-a-real-secret",
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

            services.AddSingleton<IEmailSender>(EmailSender);
            services.AddSingleton<IInviteLinkBuilder, FakeInviteLinkBuilder>();

            InvitationEmailSender = new FakeInvitationEmailSender(EmailSender);
            services.AddSingleton<IInvitationEmailSender>(InvitationEmailSender);

            services.AddSingleton<IPasswordResetEmailSender>(
                new FakePasswordResetEmailSender());

            services.AddScoped<IStripeGateway>(_ => StripeGateway);

            services.AddScoped<ISupabaseAuthGateway>(_ => SupabaseAuthGateway);

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
